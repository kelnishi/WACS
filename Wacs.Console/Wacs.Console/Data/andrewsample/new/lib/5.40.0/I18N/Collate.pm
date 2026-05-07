package I18N::Collate;use strict;our$VERSION='1.02';use POSIX qw(strxfrm LC_COLLATE);use warnings::register;require Exporter;our@ISA=qw(Exporter);our@EXPORT=qw(collate_xfrm setlocale LC_COLLATE);our@EXPORT_OK=qw();use overload qw(fallback 1 cmp collate_cmp);our($LOCALE,$C);our$please_use_I18N_Collate_even_if_deprecated=0;sub new {my$new=$_[1];if (warnings::enabled()&& $] >= 5.003_06){unless ($please_use_I18N_Collate_even_if_deprecated){warnings::warn <<___EOD___;$please_use_I18N_Collate_even_if_deprecated++}}bless \$new}sub setlocale {my ($category,$locale)=@_[0,1];POSIX::setlocale($category,$locale)if (defined$category);$LOCALE=$locale || $ENV{'LC_COLLATE'}|| $ENV{'LC_ALL'}|| ''}sub C {my$s=${$_[0]};$C->{$LOCALE}->{$s}=collate_xfrm($s)unless (defined$C->{$LOCALE}->{$s});$C->{$LOCALE}->{$s}}sub collate_xfrm {my$s=$_[0];my$x='';for (split(/(\000+)/,$s)){$x .= (/^\000/)? $_ : strxfrm("$_\000")}$x}sub collate_cmp {&C($_[0])cmp &C($_[1])}&I18N::Collate::setlocale();1;
***

  WARNING: starting from the Perl version 5.003_06
  the I18N::Collate interface for comparing 8-bit scalar data
  according to the current locale

	HAS BEEN DEPRECATED

  That is, please do not use it anymore for any new applications
  and please migrate the old applications away from it because its
  functionality was integrated into the Perl core language in the
  release 5.003_06.

  See the perllocale manual page for further information.

***
___EOD___
