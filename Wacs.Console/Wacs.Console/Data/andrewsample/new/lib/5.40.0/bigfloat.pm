package bigfloat;use strict;use warnings;use Carp qw<carp croak>;our$VERSION='0.67';use Exporter;our@ISA=qw(Exporter);our@EXPORT_OK=qw(PI e bpi bexp hex oct);our@EXPORT=qw(inf NaN);use overload;my$obj_class="Math::BigFloat";sub accuracy {my$self=shift;$obj_class -> accuracy(@_)}sub precision {my$self=shift;$obj_class -> precision(@_)}sub round_mode {my$self=shift;$obj_class -> round_mode(@_)}sub div_scale {my$self=shift;$obj_class -> div_scale(@_)}sub upgrade {my$self=shift;$obj_class -> upgrade(@_)}sub downgrade {my$self=shift;$obj_class -> downgrade(@_)}sub in_effect {my$level=shift || 0;my$hinthash=(caller($level))[10];$hinthash->{bigfloat}}sub _float_constant {my$str=shift;my$nstr;if ($str =~ /^0(?:[Oo]|_*[0-7])/ and $nstr=Math::BigInt -> oct_str_to_dec_flt_str($str)or $nstr=Math::BigInt -> dec_str_to_dec_flt_str($str)or $str =~ /^0[Xx]/ and $nstr=Math::BigInt -> hex_str_to_dec_flt_str($str)or $str =~ /^0[Bb]/ and $nstr=Math::BigInt -> bin_str_to_dec_flt_str($str)){return$obj_class -> new($nstr)}warn "Internal error: unable to handle literal constant '$str'."," This is a bug, so please report this to the module author.";return$obj_class -> bnan()}use constant LEXICAL=>$] > 5.009004;sub _hex_core {my$str=shift;my$x;if ($str =~ s/ ^ ( 0? [xX] )? ( [0-9a-fA-F]* ( _ [0-9a-fA-F]+ )* ) //x){my$chrs=$2;$chrs =~ tr/_//d;$chrs='0' unless CORE::length$chrs;$x=$obj_class -> from_hex($chrs)}else {$x=$obj_class -> bzero()}if (CORE::length($str)){require Carp;Carp::carp(sprintf("Illegal hexadecimal digit '%s' ignored",substr($str,0,1)))}return$x}sub _oct_core {my$str=shift;$str =~ s/^\s*//;return _hex_core($str)if$str =~ /^0?[xX]/;my$x;if ($str =~ /^0?[bB]/){if ($str =~ s/ ^ ( 0? [bB] )? ( [01]* ( _ [01]+ )* ) //x){my$chrs=$2;$chrs =~ tr/_//d;$chrs='0' unless CORE::length$chrs;$x=$obj_class -> from_bin($chrs)}if (CORE::length($str)){require Carp;Carp::carp(sprintf("Illegal binary digit '%s' ignored",substr($str,0,1)))}return$x}if ($str =~ s/ ^ ( 0? [oO] )? ( [0-7]* ( _ [0-7]+ )* ) //x){my$chrs=$2;$chrs =~ tr/_//d;$chrs='0' unless CORE::length$chrs;$x=$obj_class -> from_oct($chrs)}if (CORE::length($str)){require Carp;Carp::carp(sprintf("Illegal octal digit '%s' ignored",substr($str,0,1)))}return$x}{my$proto=LEXICAL ? '_' : ';$';eval '
sub hex(' .$proto .') {' .<<'.';eval '
    my $str = @_ ? $_[0] : $_;
    _hex_core($str);
}
.
sub oct(' .$proto .') {' .<<'.'}my ($prev_oct,$prev_hex,$overridden);if (LEXICAL){eval <<'.'}sub _override {return if$overridden;$prev_oct=*CORE::GLOBAL::oct{CODE};$prev_hex=*CORE::GLOBAL::hex{CODE};no warnings 'redefine';*CORE::GLOBAL::oct=\&_oct;*CORE::GLOBAL::hex=\&_hex;$overridden=1}sub unimport {delete $^H{bigfloat};overload::remove_constant('binary','','float','','integer')}sub import {my$class=shift;$^H{bigfloat}=1;delete $^H{bigint};delete $^H{bigrat};if (LEXICAL){_override()}my@import=();my@a=();my$ver;while (@_){my$param=shift;if ($param =~ /^a(ccuracy)?$/){push@import,'accuracy',shift();next}if ($param =~ /^p(recision)?$/){push@import,'precision',shift();next}if ($param eq 'round_mode'){push@import,'round_mode',shift();next}if ($param =~ /^(l|lib|try|only)$/){push@import,$param eq 'l' ? 'lib' : $param;push@import,shift()if @_;next}if ($param =~ /^(v|version)$/){$ver=1;next}if ($param =~ /^(t|trace)$/){$obj_class .= "::Trace";eval "require $obj_class";die $@ if $@;next}if ($param =~ /^(PI|e|bexp|bpi|hex|oct)\z/){push@a,$param;next}croak("Unknown option '$param'")}eval "require $obj_class";die $@ if $@;$obj_class -> import(@import);if ($ver){printf "%-31s v%s\n",$class,$class -> VERSION();printf " lib => %-23s v%s\n",$obj_class -> config("lib"),$obj_class -> config("lib_version");printf "%-31s v%s\n",$obj_class,$obj_class -> VERSION();exit}$class -> export_to_level(1,$class,@a);overload::constant integer=>sub {my$str=shift;return$obj_class -> new($str)},float=>sub {_float_constant(shift)},binary=>sub {my$str=shift;return$obj_class -> new($str)if$str =~ /^0[XxBb]/;$obj_class -> from_oct($str)}}sub inf () {$obj_class -> binf()}sub NaN () {$obj_class -> bnan()}sub PI () {$obj_class -> new('3.141592653589793238462643383279502884197')}sub e () {$obj_class -> new('2.718281828459045235360287471352662497757')}sub bpi ($) {my$up=Math::BigFloat -> upgrade();Math::BigFloat -> upgrade(undef);my$x=Math::BigFloat -> bpi(@_);Math::BigFloat -> upgrade($up);return$x}sub bexp ($$) {my$up=Math::BigFloat -> upgrade();Math::BigFloat -> upgrade(undef);my$x=Math::BigFloat -> new(shift);$x -> bexp(@_);Math::BigFloat -> upgrade($up);return$x}1;
    my $str = @_ ? $_[0] : $_;
    _oct_core($str);
}
.
sub _hex(_) {
    my $hh = (caller 0)[10];
    return $$hh{bigfloat} ? bigfloat::_hex_core($_[0])
         : $$hh{bigrat} ? bigrat::_hex_core($_[0])
         : $$hh{bigint} ? bigint::_hex_core($_[0])
         : $prev_hex    ? &$prev_hex($_[0])
         : CORE::hex($_[0]);
}

sub _oct(_) {
    my $hh = (caller 0)[10];
    return $$hh{bigfloat} ? bigfloat::_oct_core($_[0])
         : $$hh{bigrat} ? bigrat::_oct_core($_[0])
         : $$hh{bigint} ? bigint::_oct_core($_[0])
         : $prev_oct    ? &$prev_oct($_[0])
         : CORE::oct($_[0]);
}
.
