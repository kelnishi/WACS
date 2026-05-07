package Getopt::Std;use strict;use warnings;require Exporter;our@ISA=qw(Exporter);our@EXPORT=qw(getopt getopts);our$VERSION='1.14';sub getopt (;$$) {my ($argumentative,$hash)=@_;$argumentative='' if!defined$argumentative;my ($first,$rest);local $_;local@EXPORT;while (@ARGV && ($_=$ARGV[0])=~ /^-(.)(.*)/){($first,$rest)=($1,$2);if (/^--$/){shift@ARGV;last}if (index($argumentative,$first)>= 0){if ($rest ne ''){shift(@ARGV)}else {shift(@ARGV);$rest=shift(@ARGV)}if (ref$hash){$$hash{$first}=$rest}else {no strict 'refs';${"opt_$first"}=$rest;push(@EXPORT,"\$opt_$first")}}else {if (ref$hash){$$hash{$first}=1}else {no strict 'refs';${"opt_$first"}=1;push(@EXPORT,"\$opt_$first")}if ($rest ne ''){$ARGV[0]="-$rest"}else {shift(@ARGV)}}}unless (ref$hash){local$Exporter::ExportLevel=1;Getopt::Std->import}}our ($OUTPUT_HELP_VERSION,$STANDARD_HELP_VERSION);sub output_h () {return$OUTPUT_HELP_VERSION if defined$OUTPUT_HELP_VERSION;return \*STDOUT if$STANDARD_HELP_VERSION;return \*STDERR}sub try_exit () {exit 0 if$STANDARD_HELP_VERSION;my$p=__PACKAGE__;print {output_h()}<<EOM}sub version_mess ($;$) {my$args=shift;my$h=output_h;if (@_ and defined&main::VERSION_MESSAGE){main::VERSION_MESSAGE($h,__PACKAGE__,$VERSION,$args)}else {my$v=$main::VERSION;$v='[unknown]' unless defined$v;my$myv=$VERSION;$myv .= ' [paranoid]' unless$STANDARD_HELP_VERSION;my$perlv=$];$perlv=sprintf "%vd",$^V if $] >= 5.006;print$h <<EOH}}sub help_mess ($;$) {my$args=shift;my$h=output_h;if (@_ and defined&main::HELP_MESSAGE){main::HELP_MESSAGE($h,__PACKAGE__,$VERSION,$args)}else {my (@witharg)=($args =~ /(\S)\s*:/g);my (@rest)=($args =~ /([^\s:])(?!\s*:)/g);my ($help,$arg)=('','');if (@witharg){$help .= "\n\tWith arguments: -" .join " -",@witharg;$arg="\nSpace is not required between options and their arguments."}if (@rest){$help .= "\n\tBoolean (without arguments): -" .join " -",@rest}my ($scr)=($0 =~ m,([^/\\]+)$,);print$h <<EOH if @_;print$h <<EOH;my$has_pod;if (defined $0 and $0 ne '-e' and -f $0 and -r $0 and open my$script,'<',$0){while (<$script>){$has_pod=1,last if /^=(pod|head1)/}}print$h <<EOH if$has_pod}}sub getopts ($;$) {my ($argumentative,$hash)=@_;my (@args,$first,$rest,$exit);my$errs=0;local $_;local@EXPORT;@args=split(/ */,$argumentative);while(@ARGV && ($_=$ARGV[0])=~ /^-(.)(.*)/s){($first,$rest)=($1,$2);if (/^--$/){shift@ARGV;last}my$pos=index($argumentative,$first);if ($pos >= 0){if (defined($args[$pos+1])and ($args[$pos+1]eq ':')){shift(@ARGV);if ($rest eq ''){++$errs unless@ARGV;$rest=shift(@ARGV)}if (ref$hash){$$hash{$first}=$rest}else {no strict 'refs';${"opt_$first"}=$rest;push(@EXPORT,"\$opt_$first")}}else {if (ref$hash){$$hash{$first}=1}else {no strict 'refs';${"opt_$first"}=1;push(@EXPORT,"\$opt_$first")}if ($rest eq ''){shift(@ARGV)}else {$ARGV[0]="-$rest"}}}else {if ($first eq '-' and $rest eq 'help'){version_mess($argumentative,'main');help_mess($argumentative,'main');try_exit();shift(@ARGV);next}elsif ($first eq '-' and $rest eq 'version'){version_mess($argumentative,'main');try_exit();shift(@ARGV);next}warn "Unknown option: $first\n";++$errs;if ($rest ne ''){$ARGV[0]="-$rest"}else {shift(@ARGV)}}}unless (ref$hash){local$Exporter::ExportLevel=1;Getopt::Std->import}$errs==0}1;
  [Now continuing due to backward compatibility and excessive paranoia.
   See 'perldoc $p' about \$$p\::STANDARD_HELP_VERSION.]
EOM
$0 version $v calling Getopt::Std::getopts (version $myv),
running under Perl version $perlv.
EOH

Usage: $scr [-OPTIONS [-MORE_OPTIONS]] [--] [PROGRAM_ARG1 ...]
EOH

The following single-character options are accepted:$help

Options may be merged together.  -- stops processing of options.$arg
EOH

For more details run
	perldoc -F $0
EOH
