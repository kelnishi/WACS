package autodie;use 5.008;use strict;use warnings;use parent qw(Fatal);our$VERSION;BEGIN {our$VERSION='2.37'}use constant ERROR_WRONG_FATAL=>q{
Incorrect version of Fatal.pm loaded by autodie.

The autodie pragma uses an updated version of Fatal to do its
heavy lifting.  We seem to have loaded Fatal version %s, which is
probably the version that came with your version of Perl.  However
autodie needs version %s, which would have come bundled with
autodie.

You may be able to solve this problem by adding the following
line of code to your main program, before any use of Fatal or
autodie.

    use lib "%s";

};BEGIN {if (defined($Fatal::VERSION)and defined($VERSION)and $Fatal::VERSION ne $VERSION){my$autodie_path=$INC{'autodie.pm'};$autodie_path =~ s/autodie\.pm//;require Carp;Carp::croak sprintf(ERROR_WRONG_FATAL,$Fatal::VERSION,$VERSION,$autodie_path)}}sub import {splice(@_,1,0,Fatal::LEXICAL_TAG);goto&Fatal::import}sub unimport {splice(@_,1,0,Fatal::LEXICAL_TAG);goto&Fatal::unimport}1;