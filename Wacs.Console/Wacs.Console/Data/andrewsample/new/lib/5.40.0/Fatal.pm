package Fatal;use 5.008;use Carp;use strict;use warnings;use Tie::RefHash;use Config;use Scalar::Util qw(set_prototype looks_like_number);use autodie::Util qw(fill_protos install_subs make_core_trampoline on_end_of_compile_scope);use constant SMARTMATCH_ALLOWED=>($] >= 5.010 && $] < 5.041);use constant SMARTMATCH_CATEGORY=>(!SMARTMATCH_ALLOWED || $] < 5.018 ? undef : exists$warnings::Offsets{'experimental::smartmatch'}? 'experimental::smartmatch' : 'deprecated');use constant LEXICAL_TAG=>q{:lexical};use constant VOID_TAG=>q{:void};use constant INSIST_TAG=>q{!};use constant CACHE_AUTODIE_LEAK_GUARD=>0;use constant CACHE_FATAL_WRAPPER=>1;use constant CACHE_FATAL_VOID=>2;use constant ERROR_NOARGS=>'Cannot use lexical %s with no arguments';use constant ERROR_VOID_LEX=>VOID_TAG.' cannot be used with lexical scope';use constant ERROR_LEX_FIRST=>LEXICAL_TAG.' must be used as first argument';use constant ERROR_NO_LEX=>"no %s can only start with ".LEXICAL_TAG;use constant ERROR_BADNAME=>"Bad subroutine name for %s: %s";use constant ERROR_NOTSUB=>"%s is not a Perl subroutine";use constant ERROR_NOT_BUILT=>"%s is neither a builtin, nor a Perl subroutine";use constant ERROR_NOHINTS=>"No user hints defined for %s";use constant ERROR_CANT_OVERRIDE=>"Cannot make the non-overridable builtin %s fatal";use constant ERROR_NO_IPC_SYS_SIMPLE=>"IPC::System::Simple required for Fatalised/autodying system()";use constant ERROR_IPC_SYS_SIMPLE_OLD=>"IPC::System::Simple version %f required for Fatalised/autodying system().  We only have version %f";use constant ERROR_AUTODIE_CONFLICT=>q{"no autodie '%s'" is not allowed while "use Fatal '%s'" is in effect};use constant ERROR_FATAL_CONFLICT=>q{"use Fatal '%s'" is not allowed while "no autodie '%s'" is in effect};use constant ERROR_SMARTMATCH_HINTS=>q{%s hints for %s must be code, regexp, or undef. Use of other values is deprecated and only supported on Perl 5.10 through 5.40.};use constant WARNING_SMARTMATCH_DEPRECATED=>q{%s hints for %s must be code, regexp, or undef. Use of other values is deprecated and will be removed before Perl 5.42.};use constant MIN_IPC_SYS_SIMPLE_VER=>0.12;our$VERSION='2.37';our$Debug ||= 0;our%_EWOULDBLOCK=(MSWin32=>33,);$Carp::CarpInternal{'Fatal'}=1;$Carp::CarpInternal{'autodie'}=1;$Carp::CarpInternal{'autodie::exception'}=1;my$try_EAGAIN=($^O eq 'linux' and $Config{archname}=~ /hppa|parisc/)? 1 : 0;my%TAGS=(':io'=>[qw(:dbm :file :filesys :ipc :socket read seek sysread syswrite sysseek)],':dbm'=>[qw(dbmopen dbmclose)],':file'=>[qw(open close flock sysopen fcntl binmode ioctl truncate)],':filesys'=>[qw(opendir closedir chdir link unlink rename mkdir symlink rmdir readlink chmod chown utime)],':ipc'=>[qw(:msg :semaphore :shm pipe kill)],':msg'=>[qw(msgctl msgget msgrcv msgsnd)],':threads'=>[qw(fork)],':semaphore'=>[qw(semctl semget semop)],':shm'=>[qw(shmctl shmget shmread)],':system'=>[qw(system exec)],':socket'=>[qw(accept bind connect getsockopt listen recv send setsockopt shutdown socketpair)],':default'=>[qw(:io :threads)],':v207'=>[qw(:threads :dbm :socket read seek sysread syswrite sysseek open close flock sysopen fcntl fileno binmode ioctl truncate opendir closedir chdir link unlink rename mkdir symlink rmdir readlink umask :msg :semaphore :shm pipe)],':v213'=>[qw(:v207 chmod)],':v214'=>[qw(:v213 chown utime kill)],':v225'=>[qw(:io :threads umask fileno)],':1.994'=>[qw(:v207)],':1.995'=>[qw(:v207)],':1.996'=>[qw(:v207)],':1.997'=>[qw(:v207)],':1.998'=>[qw(:v207)],':1.999'=>[qw(:v207)],':1.999_01'=>[qw(:v207)],':2.00'=>[qw(:v207)],':2.01'=>[qw(:v207)],':2.02'=>[qw(:v207)],':2.03'=>[qw(:v207)],':2.04'=>[qw(:v207)],':2.05'=>[qw(:v207)],':2.06'=>[qw(:v207)],':2.06_01'=>[qw(:v207)],':2.07'=>[qw(:v207)],':2.08'=>[qw(:v213)],':2.09'=>[qw(:v213)],':2.10'=>[qw(:v213)],':2.11'=>[qw(:v213)],':2.12'=>[qw(:v213)],':2.13'=>[qw(:v213)],':2.14'=>[qw(:v225)],':2.15'=>[qw(:v225)],':2.16'=>[qw(:v225)],':2.17'=>[qw(:v225)],':2.18'=>[qw(:v225)],':2.19'=>[qw(:v225)],':2.20'=>[qw(:v225)],':2.21'=>[qw(:v225)],':2.22'=>[qw(:v225)],':2.23'=>[qw(:v225)],':2.24'=>[qw(:v225)],':2.25'=>[qw(:v225)],':2.26'=>[qw(:default)],':2.27'=>[qw(:default)],':2.28'=>[qw(:default)],':2.29'=>[qw(:default)],':2.30'=>[qw(:default)],':2.31'=>[qw(:default)],':2.32'=>[qw(:default)],':2.33'=>[qw(:default)],':2.34'=>[qw(:default)],':2.35'=>[qw(:default)],':2.36'=>[qw(:default)],':2.37'=>[qw(:default)],);{my%seen;my@all=grep {!/^:/ &&!$seen{$_}++}map {@{$_}}values%TAGS;$TAGS{':all'}=\@all}my%Use_defined_or;@Use_defined_or{qw(CORE::fork CORE::recv CORE::send CORE::open CORE::fileno CORE::read CORE::readlink CORE::sysread CORE::syswrite CORE::sysseek CORE::umask)}=();my%Returns_num_things_changed=('CORE::chmod'=>1,'CORE::chown'=>2,'CORE::kill'=>1,'CORE::unlink'=>0,'CORE::utime'=>2,);my%Retval_action=("CORE::open"=>q{

    # apply the open pragma from our caller
    if( defined $retval && !( @_ >= 3 && $_[1] =~ /:/ )) {
        # Get the caller's hint hash
        my $hints = (caller 0)[10];

        # Decide if we're reading or writing and apply the appropriate encoding
        # These keys are undocumented.
        # Match what PerlIO_context_layers() does.  Read gets the read layer,
        # everything else gets the write layer.
        my $encoding = $_[1] =~ /^\+?>/ ? $hints->{"open>"} : $hints->{"open<"};

        # Apply the encoding, if any.
        if( $encoding ) {
            binmode $_[0], $encoding;
        }
    }

},"CORE::sysopen"=>q{

    # apply the open pragma from our caller
    if( defined $retval ) {
        # Get the caller's hint hash
        my $hints = (caller 0)[10];

        require Fcntl;

        # Decide if we're reading or writing and apply the appropriate encoding.
        # Match what PerlIO_context_layers() does.  Read gets the read layer,
        # everything else gets the write layer.
        my $open_read_only = !($_[2] ^ Fcntl::O_RDONLY());
        my $encoding = $open_read_only ? $hints->{"open<"} : $hints->{"open>"};

        # Apply the encoding, if any.
        if( $encoding ) {
            binmode $_[0], $encoding;
        }
    }

},);my%reusable_builtins;@reusable_builtins{qw(CORE::fork CORE::kill CORE::truncate CORE::chdir CORE::link CORE::unlink CORE::rename CORE::mkdir CORE::symlink CORE::rmdir CORE::readlink CORE::umask CORE::chmod CORE::chown CORE::utime CORE::msgctl CORE::msgget CORE::msgrcv CORE::msgsnd CORE::semctl CORE::semget CORE::semop CORE::shmctl CORE::shmget CORE::shmread CORE::exec CORE::system)}=();my%Cached_fatalised_sub=();my%Package_Fatal=();my%Original_user_sub=();my%Is_fatalised_sub=();tie%Is_fatalised_sub,'Tie::RefHash';my%Trampoline_cache;my%CORE_prototype_cache;my$PACKAGE=__PACKAGE__;my$NO_PACKAGE="no $PACKAGE";sub import {my$class=shift(@_);my@original_args=@_;my$void=0;my$lexical=0;my$insist_hints=0;my ($pkg,$filename)=caller();@_ or return;if ($_[0]eq LEXICAL_TAG){$lexical=1;shift @_;if ($class ne 'autodie' and not $class->isa('autodie')){if ($class eq 'Fatal'){warnings::warnif('deprecated','[deprecated] The "use Fatal qw(:lexical ...)" ' .'should be replaced by "use autodie qw(...)". ' .'Seen')}else {warnings::warnif('deprecated',"[deprecated] The class/Package $class is a " .'subclass of Fatal and used the :lexical. ' .'If $class provides lexical error checking ' .'it should extend autodie instead of using :lexical. ' .'Seen')}$class='autodie';require autodie}if (@_==0){push(@_,':default')}if (grep {$_ eq VOID_TAG}@_){croak(ERROR_VOID_LEX)}}if (grep {$_ eq LEXICAL_TAG}@_){croak(ERROR_LEX_FIRST)}my@fatalise_these=@_;my%unload_later;my%install_subs;for my$func ($class->_translate_import_args(@fatalise_these)){if ($func eq VOID_TAG){$void=1}elsif ($func eq INSIST_TAG){$insist_hints=1}else {my$insist_this=$insist_hints;if (substr($func,0,1)eq '!'){$func=substr($func,1);$insist_this=1}my$sub=$func;$sub="${pkg}::$sub" unless$sub =~ /::/;if (!$lexical and $^H{$NO_PACKAGE}{$sub}){croak(sprintf(ERROR_FATAL_CONFLICT,$func,$func))}my$sub_ref=$class->_make_fatal($func,$pkg,$void,$lexical,$filename,$insist_this,\%install_subs,);$Original_user_sub{$sub}||= $sub_ref;$unload_later{$func}=$sub_ref if$lexical}}install_subs($pkg,\%install_subs);if ($lexical){$^H |= 0x020000;on_end_of_compile_scope(sub {install_subs($pkg,\%unload_later)});$^H{autodie}="$PACKAGE @original_args"}return}sub unimport {my$class=shift;if ($_[0]ne LEXICAL_TAG){croak(sprintf(ERROR_NO_LEX,$class))}shift @_;my$pkg=(caller)[0];my@unimport_these=@_ ? @_ : ':all';my (%uninstall_subs,%reinstall_subs);for my$symbol ($class->_translate_import_args(@unimport_these)){my$sub=$symbol;$sub="${pkg}::$sub" unless$sub =~ /::/;if (exists$Package_Fatal{$sub}){croak(sprintf(ERROR_AUTODIE_CONFLICT,$symbol,$symbol))}$^H{$NO_PACKAGE}{$sub}=1;{no strict 'refs';$reinstall_subs{$symbol}=\&$sub if exists ${"${pkg}::"}{$symbol}}$uninstall_subs{$symbol}=$Original_user_sub{$sub}}install_subs($pkg,\%uninstall_subs);on_end_of_compile_scope(sub {install_subs($pkg,\%reinstall_subs)});return}sub _translate_import_args {my ($class,@args)=@_;my@result;my%seen;if (@args < 2){return unless@args;return@args unless exists($TAGS{$args[0]});return map {substr($_,6)}@{$class->_expand_tag($args[0])}}for my$a (reverse(@args)){if (exists$TAGS{$a}){my$expanded=$class->_expand_tag($a);push(@result,grep {!$seen{$_}++}map {substr($_,6)}reverse(@{$expanded}))}else {my$letter=substr($a,0,1);if ($letter ne ':' && $a ne INSIST_TAG){next if$seen{$a}++;if ($letter eq '!' and $seen{substr($a,1)}++){my$name=substr($a,1);@result=grep {$_ ne $name}@result}}push@result,$a}}return reverse(@result)}{my%tag_cache=('all'=>[map {"CORE::$_"}@{$TAGS{':all'}}],);sub _expand_tag {my ($class,$tag)=@_;if (my$cached=$tag_cache{$tag}){return$cached}if (not exists$TAGS{$tag}){croak "Invalid exception class $tag"}my@to_process=@{$TAGS{$tag}};if (@to_process==1 && substr($to_process[0],0,1)eq ':'){my$expanded=$class->_expand_tag($to_process[0]);$tag_cache{$tag}=$expanded;return$expanded}my%seen=();my@taglist=();for my$item (@to_process){if (substr($item,0,1)eq ':'){my$expanded=$class->_expand_tag($item);push@taglist,grep {!$seen{$_}++}@{$expanded}}else {my$subname="CORE::$item";push@taglist,$subname unless$seen{$subname}++}}$tag_cache{$tag}=\@taglist;return \@taglist}}sub write_invocation {my ($core,$call,$name,$void,@args)=@_;return Fatal->_write_invocation($core,$call,$name,$void,0,undef,undef,@args)}sub _write_invocation {my ($class,$core,$call,$name,$void,$lexical,$sub,$sref,@argvs)=@_;if (@argvs==1){my@argv=@{$argvs[0]};shift@argv;return$class->_one_invocation($core,$call,$name,$void,$sub,!$lexical,$sref,@argv)}else {my$else="\t";my (@out,@argv,$n);while (@argvs){@argv=@{shift@argvs};$n=shift@argv;my$condition="\@_ == $n";if (@argv and $argv[-1]=~ /[#@]_/){$condition="\@_ >= $n"}push@out,"${else}if ($condition) {\n";$else="\t} els";push@out,$class->_one_invocation($core,$call,$name,$void,$sub,!$lexical,$sref,@argv)}push@out,qq[
            }
            die "Internal error: $name(\@_): Do not expect to get ", scalar(\@_), " arguments";
    ];return join '',@out}}sub one_invocation {my ($core,$call,$name,$void,@argv)=@_;return Fatal->_one_invocation($core,$call,$name,$void,undef,1,undef,@argv)}sub _one_invocation {my ($class,$core,$call,$name,$void,$sub,$back_compat,$sref,@argv)=@_;if ($void and not $back_compat){Carp::confess("Internal error: :void mode not supported with $class")}if ($back_compat){if ($call eq 'CORE::system'){return q{
                croak("UNIMPLEMENTED: use Fatal qw(system) not supported.");
            }}local $"=', ';if ($void){return qq/return (defined wantarray)?$call(@argv):
                   $call(@argv) || Carp::croak("Can't $name(\@_)/ .($core ? ': $!' : ', \$! is \"$!\"').'")'}else {return qq{return $call(@argv) || Carp::croak("Can't $name(\@_)} .($core ? ': $!' : ', \$! is \"$!\"').'")'}}my$human_sub_name=$core ? $call : $sub;my$use_defined_or;my$hints;if ($core){$use_defined_or=exists ($Use_defined_or{$call})}else {require autodie::hints;$hints=autodie::hints->get_hints_for($sref);$human_sub_name=autodie::hints->sub_fullname($sref)}if ($call eq 'CORE::system'){local $"=", ";return qq{
            my \$retval;
            my \$E;


            {
                local \$@;

                eval {
                    \$retval = IPC::System::Simple::system(@argv);
                };

                \$E = \$@;
            }

            if (\$E) {

                # TODO - This can't be overridden in child
                # classes!

                die autodie::exception::system->new(
                    function => q{CORE::system}, args => [ @argv ],
                    message => "\$E", errno => \$!,
                );
            }

            return \$retval;
        }}local $"=', ';my$die=qq{
        die $class->throw(
            function => q{$human_sub_name}, args => [ @argv ],
            pragma => q{$class}, errno => \$!,
            context => \$context, return => \$retval,
            eval_error => \$@
        )
    };if ($call eq 'CORE::flock'){require POSIX;local $@;my$EWOULDBLOCK=eval {POSIX::EWOULDBLOCK()}|| $_EWOULDBLOCK{$^O}|| _autocroak("Internal error - can't overload flock - EWOULDBLOCK not defined on this system.");my$EAGAIN=$EWOULDBLOCK;if ($try_EAGAIN){$EAGAIN=eval {POSIX::EAGAIN()}|| _autocroak("Internal error - can't overload flock - EAGAIN not defined on this system.")}require Fcntl;return qq{

            my \$context = wantarray() ? "list" : "scalar";

            # Try to flock.  If successful, return it immediately.

            my \$retval = $call(@argv);
            return \$retval if \$retval;

            # If we failed, but we're using LOCK_NB and
            # returned EWOULDBLOCK, it's not a real error.

            if (\$_[1] & Fcntl::LOCK_NB() and
                (\$! == $EWOULDBLOCK or
                ($try_EAGAIN and \$! == $EAGAIN ))) {
                return \$retval;
            }

            # Otherwise, we failed.  Die noisily.

            $die;

        }}if ($call eq 'CORE::kill'){return qq[

            my \$num_things = \@_ - $Returns_num_things_changed{$call};
            my \$context = ! defined wantarray() ? 'void' : 'scalar';
            my \$signal = \$_[0];
            my \$retval = $call(@argv);
            my \$sigzero = looks_like_number( \$signal ) && \$signal == 0;

            if (    (   \$sigzero && \$context eq 'void' )
                 or ( ! \$sigzero && \$retval != \$num_things ) ) {

                $die;
            }

            return \$retval;
        ]}if (exists$Returns_num_things_changed{$call}){return qq[
            my \$num_things = \@_ - $Returns_num_things_changed{$call};
            my \$retval = $call(@argv);

            if (\$retval != \$num_things) {

                # We need \$context to throw an exception.
                # It's *always* set to scalar, because that's how
                # autodie calls chown() above.

                my \$context = "scalar";
                $die;
            }

            return \$retval;
        ]}my$code=qq[
        no warnings qw(unopened uninitialized numeric);

        if (wantarray) {
            my \@results = $call(@argv);
            my \$retval  = \\\@results;
            my \$context = "list";

    ];my$retval_action=$Retval_action{$call}|| '';if ($hints && exists$hints->{list}){my$match;if (ref($hints->{list})eq 'CODE'){$match=q[ $hints->{list}->(@results) ]}elsif (ref($hints->{list})eq 'Regexp'){$match=q[ grep $_ =~ $hints->{list}, @results ]}elsif (!defined$hints->{list}){$match=q[ grep !defined, @results ]}elsif (SMARTMATCH_ALLOWED){$match=q[ @results ~~ $hints->{list} ];warnings::warnif('deprecated',sprintf(WARNING_SMARTMATCH_DEPRECATED,'list',$sub));if (SMARTMATCH_CATEGORY){$match=sprintf q[ do { no warnings '%s'; %s } ],SMARTMATCH_CATEGORY,$match}}else {croak sprintf(ERROR_SMARTMATCH_HINTS,'list',$sub)}$code .= qq{
            if ( $match ) { $die };
        }}else {$code .= qq{
            # An empty list, or a single undef is failure
            if (! \@results or (\@results == 1 and ! defined \$results[0])) {
                $die;
            }
        }}$code .= qq[
            return \@results;
        }
    ];$code .= qq{
        my \$retval  = $call(@argv);
        my \$context = "scalar";
    };if ($hints && exists$hints->{scalar}){my$match;if (ref($hints->{scalar})eq 'CODE'){$match=q[ $hints->{scalar}->($retval) ]}elsif (ref($hints->{scalar})eq 'Regexp'){$match=q[ $retval =~ $hints->{scalar} ]}elsif (!defined$hints->{scalar}){$match=q[ !defined $retval ]}elsif (SMARTMATCH_ALLOWED){$match=q[ $retval ~~ $hints->{scalar} ];warnings::warnif('deprecated',sprintf(WARNING_SMARTMATCH_DEPRECATED,'scalar',$sub));if (SMARTMATCH_CATEGORY){$match=sprintf q[ do { no warnings '%s'; %s } ],SMARTMATCH_CATEGORY,$match}}else {croak sprintf(ERROR_SMARTMATCH_HINTS,'scalar',$sub)}return$code .qq{
            if ( $match ) { $die };
            $retval_action
            return \$retval;
        }}return$code .($use_defined_or ? qq{

        $die if not defined \$retval;
        $retval_action
        return \$retval;

    } : qq{

        $retval_action
        return \$retval || $die;

    })}sub _make_fatal {my($class,$sub,$pkg,$void,$lexical,$filename,$insist,$install_subs)=@_;my($code,$sref,$proto,$core,$call,$hints,$cache,$cache_type);my$ini=$sub;my$name=$sub;if (index($sub,'::')==-1){$sub="${pkg}::$sub";if (substr($name,0,1)eq '&'){$name=substr($name,1)}}else {$name =~ s/.*:://}if (not $lexical){$Package_Fatal{$sub}=1}warn "# _make_fatal: sub=$sub pkg=$pkg name=$name void=$void\n" if$Debug;croak(sprintf(ERROR_BADNAME,$class,$name))unless$name =~ /^\w+$/;if (defined(&$sub)){$sref=\&$sub;if ($Package_Fatal{$sub}and exists($CORE_prototype_cache{"CORE::$name"})){$core=1;$call="CORE::$name";$proto=$CORE_prototype_cache{$call}}else {if (exists($Is_fatalised_sub{$sref})){my$s=$Is_fatalised_sub{$sref};if (defined($s)){$sub=$s}else {$core=1;$call="CORE::$name";$proto=$CORE_prototype_cache{$call}}}if (!$core){$proto=prototype($sref);$call='&$sref';require autodie::hints;$hints=autodie::hints->get_hints_for($sref);if ($insist and not $hints){croak(sprintf(ERROR_NOHINTS,$name))}$hints ||= autodie::hints::DEFAULT_HINTS()}}}elsif ($sub eq $ini && $sub !~ /^CORE::GLOBAL::/){croak(sprintf(ERROR_NOTSUB,$sub))}elsif ($name eq 'system'){my$E;{local $@;eval {require IPC::System::Simple;require autodie::exception::system};$E=$@}if ($E){croak ERROR_NO_IPC_SYS_SIMPLE}if ($IPC::System::Simple::VERSION < MIN_IPC_SYS_SIMPLE_VER){croak sprintf(ERROR_IPC_SYS_SIMPLE_OLD,MIN_IPC_SYS_SIMPLE_VER,$IPC::System::Simple::VERSION)}$call='CORE::system';$core=1}elsif ($name eq 'exec'){$call='CORE::exec';$core=1}else {$call="CORE::$name";if (exists($CORE_prototype_cache{$call})){$proto=$CORE_prototype_cache{$call}}else {my$E;{local $@;$proto=eval {prototype$call};$E=$@}croak(sprintf(ERROR_NOT_BUILT,$name))if$E;croak(sprintf(ERROR_CANT_OVERRIDE,$name))if not defined$proto;$CORE_prototype_cache{$call}=$proto}$core=1}$cache=$Cached_fatalised_sub{$class}{$sub};if ($lexical){$cache_type=CACHE_AUTODIE_LEAK_GUARD}else {$cache_type=CACHE_FATAL_WRAPPER;$cache_type=CACHE_FATAL_VOID if$void}if (my$subref=$cache->{$cache_type}){$install_subs->{$name}=$subref;return$sref}if ($core && exists$reusable_builtins{$call}){$code=$reusable_builtins{$call}{$lexical};if (!$lexical && defined($code)){$install_subs->{$name}=$code;return$sref}}if (!($lexical && $core)&&!defined($code)){my$wrapper_pkg=$pkg;$wrapper_pkg=undef if (exists($reusable_builtins{$call}));$code=$class->_compile_wrapper($wrapper_pkg,$core,$call,$name,$void,$lexical,$sub,$sref,$hints,$proto);if (!defined($wrapper_pkg)){$reusable_builtins{$call}{$lexical}=$code}}my$installed_sub=$code;if ($lexical){$installed_sub=$class->_make_leak_guard($filename,$code,$sref,$call,$pkg,$proto)}$cache->{$cache_type}=$code;$install_subs->{$name}=$installed_sub;$Is_fatalised_sub{$installed_sub}=$sref;return$sref}sub exception_class {return "autodie::exception"};{my%exception_class_for;my%class_loaded;sub throw {my ($class,@args)=@_;my$exception_class=$exception_class_for{$class}||= $class->exception_class;if (not $class_loaded{$exception_class}){if ($exception_class =~ /[^\w:']/){confess "Bad exception class '$exception_class'.\nThe '$class->exception_class' method wants to use $exception_class\nfor exceptions, but it contains characters which are not word-characters or colons."}my$E;{local $@;my$pm_file=$exception_class .".pm";$pm_file =~ s{ (?: :: | ' ) }{/}gx;eval {require$pm_file};$E=$@}confess "Failed to load '$exception_class'.\nThis may be a typo in the '$class->exception_class' method,\nor the '$exception_class' module may not exist.\n\n $E" if$E;$class_loaded{$exception_class}++}return$exception_class->new(@args)}}sub _make_leak_guard {my ($class,$filename,$wrapped_sub,$orig_sub,$call,$pkg,$proto)=@_;my$leak_guard=sub {my$caller_level=0;my$caller;while (($caller=(caller$caller_level)[1])=~ m{^\(eval \d+\)$}){last if ($caller eq $filename);$caller_level++}if ($caller eq $filename){if (!defined($wrapped_sub)){die "$call is not CORE::<something>" if substr($call,0,6)ne 'CORE::';my$name=substr($call,6);my$sub=$name;my$lexical=1;my$wrapper_pkg=$pkg;my$code;if (exists($reusable_builtins{$call})){$code=$reusable_builtins{$call}{$lexical};$wrapper_pkg=undef}if (!defined($code)){$code=$class->_compile_wrapper($wrapper_pkg,1,$call,$name,0,$lexical,$sub,undef,undef,$proto);if (!defined($wrapper_pkg)){$reusable_builtins{$call}{$lexical}=$code}}$wrapped_sub=$code}goto$wrapped_sub}goto$orig_sub if defined($orig_sub);$pkg='Fatal' if exists$reusable_builtins{$call};$orig_sub=$Trampoline_cache{$pkg}{$call};if (not $orig_sub){$orig_sub=make_core_trampoline($call,$pkg,$proto);$Trampoline_cache{$pkg}{$call}=$orig_sub}goto$orig_sub};if (defined$proto){set_prototype(\&$leak_guard,$proto)}return$leak_guard}sub _compile_wrapper {my ($class,$wrapper_pkg,$core,$call,$name,$void,$lexical,$sub,$sref,$hints,$proto)=@_;my$real_proto='';my@protos;my$code;if (defined$proto){$real_proto=" ($proto)"}else {$proto='@'}@protos=fill_protos($proto);$code=qq[
        sub$real_proto {
    ];if (!$lexical){$code .= q[
           local($", $!) = (', ', 0);
        ]}$code .= "no warnings qw(exec);\n" if$call eq "CORE::exec";$code .= $class->_write_invocation($core,$call,$name,$void,$lexical,$sub,$sref,@protos);$code .= "}\n";warn$code if$Debug;my$E;{no strict 'refs';local $@;if (defined($wrapper_pkg)){$code=eval("package $wrapper_pkg; require Carp; $code")}else {$code=eval("require Carp; $code")}$E=$@}if (not $code){my$true_name=$core ? $call : $sub;croak("Internal error in autodie/Fatal processing $true_name: $E")}return$code}sub _autocroak {warn Carp::longmess(@_);exit(255)}1;