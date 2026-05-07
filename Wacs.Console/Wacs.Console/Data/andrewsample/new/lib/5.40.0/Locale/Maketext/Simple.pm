package Locale::Maketext::Simple;$Locale::Maketext::Simple::VERSION='0.21_01';use strict;use 5.005;sub import {my ($class,%args)=@_;$args{Class}||= caller;$args{Style}||= 'maketext';$args{Export}||= 'loc';$args{Subclass}||= 'I18N';my ($loc,$loc_lang)=$class->load_loc(%args);$loc ||= $class->default_loc(%args);no strict 'refs';*{caller(0)."::$args{Export}"}=$loc if$args{Export};*{caller(0)."::$args{Export}_lang"}=$loc_lang || sub {1}}my%Loc;sub reload_loc {%Loc=()}sub load_loc {my ($class,%args)=@_;my$pkg=join('::',grep {defined and length}$args{Class},$args{Subclass});return$Loc{$pkg}if exists$Loc{$pkg};eval {local@INC=@INC;pop@INC if$INC[-1]eq '.';require Locale::Maketext::Lexicon;1}or return;$Locale::Maketext::Lexicon::VERSION > 0.20 or return;eval {require File::Spec;1}or return;my$path=$args{Path}|| $class->auto_path($args{Class})or return;my$pattern=File::Spec->catfile($path,'*.[pm]o');my$decode=$args{Decode}|| 0;my$encoding=$args{Encoding}|| undef;$decode=1 if$encoding;$pattern =~ s{\\}{/}g;eval "
	package $pkg;
	use base 'Locale::Maketext';
	Locale::Maketext::Lexicon->import({
	    'i-default' => [ 'Auto' ],
	    '*'	=> [ Gettext => \$pattern ],
	    _decode => \$decode,
	    _encoding => \$encoding,
	});
	*${pkg}::Lexicon = \\%${pkg}::i_default::Lexicon;
	*tense = sub { \$_[1] . ((\$_[2] eq 'present') ? 'ing' : 'ed') }
	    unless defined &tense;

	1;
    " or die $@;my$lh=eval {$pkg->get_handle}or return;my$style=lc($args{Style});if ($style eq 'maketext'){$Loc{$pkg}=sub {$lh->maketext(@_)}}elsif ($style eq 'gettext'){$Loc{$pkg}=sub {my$str=shift;$str =~ s{([\~\[\]])}{~$1}g;$str =~ s{
                ([%\\]%)                        # 1 - escaped sequence
            |
                %   (?:
                        ([A-Za-z#*]\w*)         # 2 - function call
                            \(([^\)]*)\)        # 3 - arguments
                    |
                        ([1-9]\d*|\*)           # 4 - variable
                    )
            }{
                $1 ? $1
                   : $2 ? "\[$2,"._unescape($3)."]"
                        : "[_$4]"
            }egx;return$lh->maketext($str,@_)}}else {die "Unknown Style: $style"}return$Loc{$pkg},sub {$lh=$pkg->get_handle(@_)}}sub default_loc {my ($self,%args)=@_;my$style=lc($args{Style});if ($style eq 'maketext'){return sub {my$str=shift;$str =~ s{((?<!~)(?:~~)*)\[_([1-9]\d*|\*)\]}
                     {$1%$2}g;$str =~ s{((?<!~)(?:~~)*)\[([A-Za-z#*]\w*),([^\]]+)\]}
                     {"$1%$2(" . _escape($3) . ')'}eg;_default_gettext($str,@_)}}elsif ($style eq 'gettext'){return \&_default_gettext}else {die "Unknown Style: $style"}}sub _default_gettext {my$str=shift;$str =~ s{
	%			# leading symbol
	(?:			# either one of
	    \d+			#   a digit, like %1
	    |			#     or
	    (\w+)\(		#   a function call -- 1
		(?:		#     either
		    %\d+	#	an interpolation
		    |		#     or
		    ([^,]*)	#	some string -- 2
		)		#     end either
		(?:		#     maybe followed
		    ,		#       by a comma
		    ([^),]*)	#       and a param -- 3
		)?		#     end maybe
		(?:		#     maybe followed
		    ,		#       by another comma
		    ([^),]*)	#       and a param -- 4
		)?		#     end maybe
		[^)]*		#     and other ignorable params
	    \)			#   closing function call
	)			# closing either one of
    }{
	my $digit = $2 || shift;
	$digit . (
	    $1 ? (
		($1 eq 'tense') ? (($3 eq 'present') ? 'ing' : 'ed') :
		($1 eq 'quant') ? ' ' . (($digit > 1) ? ($4 || "$3s") : $3) :
		''
	    ) : ''
	);
    }egx;return$str};sub _escape {my$text=shift;$text =~ s/\b_([1-9]\d*)/%$1/g;return$text}sub _unescape {join(',',map {/\A(\s*)%([1-9]\d*|\*)(\s*)\z/ ? "$1_$2$3" : $_}split(/,/,$_[0]))}sub auto_path {my ($self,$calldir)=@_;$calldir =~ s#::#/#g;my$path=$INC{$calldir .'.pm'}or return;if ($^O eq 'MacOS'){(my$malldir=$calldir)=~ tr#/#:#;$path =~ s#^(.*)$malldir\.pm\z#$1auto:$malldir:#s}else {$path =~ s#^(.*)$calldir\.pm\z#$1auto/$calldir/#}return$path if -d $path;$path="auto/$calldir/";for my$inc (@INC){return "$inc/$path" if -d "$inc/$path"}return}1;