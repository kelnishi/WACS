package SelfLoader;use 5.008;use strict;use IO::Handle;our$VERSION="1.27";our$AttrList;BEGIN {if ($] > 5.009004){eval <<'NEWERPERL'}else {eval <<'OLDERPERL'}}use Exporter;our@ISA=qw(Exporter);our@EXPORT=qw(AUTOLOAD);sub Version {$VERSION}sub DEBUG () {0}my%Cache;sub croak {{local $@;require Carp}goto&Carp::croak}sub carp {{local $@;require Carp}goto&Carp::carp}AUTOLOAD {our$AUTOLOAD;print STDERR "SelfLoader::AUTOLOAD for $AUTOLOAD\n" if DEBUG;my$SL_code=$Cache{$AUTOLOAD};my$save=$@;unless ($SL_code){$AUTOLOAD =~ m/^(.*)::/;SelfLoader->_load_stubs($1)unless exists$Cache{"${1}::<DATA"};$SL_code=$Cache{$AUTOLOAD};$SL_code="sub $AUTOLOAD { }" if (!$SL_code and $AUTOLOAD =~ m/::DESTROY$/);croak "Undefined subroutine $AUTOLOAD" unless$SL_code}print STDERR "SelfLoader::AUTOLOAD eval: $SL_code\n" if DEBUG;{no strict;eval$SL_code}if ($@){$@ =~ s/ at .*\n//;croak $@}$@=$save;defined(&$AUTOLOAD)|| die "SelfLoader inconsistency error";delete$Cache{$AUTOLOAD};goto &$AUTOLOAD}sub load_stubs {shift->_load_stubs((caller)[0])}sub _load_stubs {my($self,$callpack,$endlines)=@_;no strict "refs";my$fh=\*{"${callpack}::DATA"};use strict;my$currpack=$callpack;my($line,$name,@lines,@stubs,$protoype);print STDERR "SelfLoader::load_stubs($callpack)\n" if DEBUG;croak("$callpack doesn't contain an __DATA__ token")unless defined fileno($fh);if(sysseek($fh,tell($fh),0)){open my$nfh,'<&',$fh or croak "reopen: $!";close$fh or die "close: $!";open$fh,'<&',$nfh or croak "reopen2: $!";close$nfh or die "close after reopen: $!";$fh->untaint}$Cache{"${currpack}::<DATA"}=1;local($/)="\n";while(defined($line=<$fh>)and $line !~ m/^__END__/){if ($line =~ m/ ^\s*                        # indentation
use 5.009005; # due to new regexp features
# allow checking for valid ': attrlist' attachments
# see also AutoSplit
$AttrList = qr{
    \s* : \s*
    (?:
	# one attribute
	(?> # no backtrack
	    (?! \d) \w+
	    (?<nested> \( (?: [^()]++ | (?&nested)++ )*+ \) ) ?
	)
	(?: \s* : \s* | \s+ (?! :) )
    )*
}x;

NEWERPERL
# allow checking for valid ': attrlist' attachments
# (we use 'our' rather than 'my' here, due to the rather complex and buggy
# behaviour of lexicals with qr// and (??{$lex}) )
our $nested;
$nested = qr{ \( (?: (?> [^()]+ ) | (??{ $nested }) )* \) }x;
our $one_attr = qr{ (?> (?! \d) \w+ (?:$nested)? ) (?:\s*\:\s*|\s+(?!\:)) }x;
$AttrList = qr{ \s* : \s* (?: $one_attr )* }x;
OLDERPERL
	                sub\s+([\w:]+)\s*           # 'sub' and sub name
	                (
	                 (?:\([\\\$\@\%\&\*\;]*\))? # optional prototype sigils
	                 (?:$AttrList)?             # optional attribute list
	                )/x){push(@stubs,$self->_add_to_cache($name,$currpack,\@lines,$protoype));$protoype=$2;@lines=($line);if (index($1,'::')==-1){$name="${currpack}::$1"}else {$name=$1;$name =~ m/^(.*)::/;if (defined(&{"${1}::AUTOLOAD"})){\&{"${1}::AUTOLOAD"}==\&SelfLoader::AUTOLOAD || die 'SelfLoader Error: attempt to specify Selfloading'," sub $name in non-selfloading module $1"}else {$self->export($1,'AUTOLOAD')}}}elsif ($line =~ m/^package\s+([\w:]+)/){push(@stubs,$self->_add_to_cache($name,$currpack,\@lines,$protoype));$self->_package_defined($line);$name='';@lines=();$currpack=$1;$Cache{"${currpack}::<DATA"}=1;if (defined(&{"${1}::AUTOLOAD"})){\&{"${1}::AUTOLOAD"}==\&SelfLoader::AUTOLOAD || die 'SelfLoader Error: attempt to specify Selfloading'," package $currpack which already has AUTOLOAD"}else {$self->export($currpack,'AUTOLOAD')}}else {push(@lines,$line)}}if (defined($line)&& $line =~ /^__END__/){unless ($line =~ /^__END__\s*DATA/){if ($endlines){@$endlines=<$fh>}close($fh)}}push(@stubs,$self->_add_to_cache($name,$currpack,\@lines,$protoype));no strict;eval join('',@stubs)if@stubs}sub _add_to_cache {my($self,$fullname,$pack,$lines,$protoype)=@_;return ()unless$fullname;carp("Redefining sub $fullname")if exists$Cache{$fullname};$Cache{$fullname}=join('',"\n\#line 1 \"sub $fullname\"\npackage $pack; ",@$lines);print STDERR "SelfLoader cached $fullname: $Cache{$fullname}" if DEBUG;defined($protoype)? "sub $fullname $protoype;" : "sub $fullname;"}sub _package_defined {}1;