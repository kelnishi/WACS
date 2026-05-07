package Encode::MIME::Header;use strict;use warnings;our$VERSION=do {my@r=(q$Revision: 2.29 $ =~ /\d+/g);sprintf "%d." ."%02d" x $#r,@r};use Carp ();use Encode ();use MIME::Base64 ();my%seed=(decode_b=>1,decode_q=>1,encode=>'B',charset=>'UTF-8',bpl=>75,);my@objs;push@objs,bless {%seed,Name=>'MIME-Header',}=>__PACKAGE__;push@objs,bless {%seed,decode_q=>0,Name=>'MIME-B',}=>__PACKAGE__;push@objs,bless {%seed,decode_b=>0,encode=>'Q',Name=>'MIME-Q',}=>__PACKAGE__;Encode::define_encoding($_,$_->{Name})foreach@objs;use parent qw(Encode::Encoding);sub needs_lines {1}sub perlio_ok {0}my$re_charset=qr/[!"#\$%&'+\-0-9A-Z\\\^_`a-z\{\|\}~]+/;my$re_language=qr/[A-Za-z]{1,8}(?:-[0-9A-Za-z]{1,8})*/;my$re_encoding=qr/[QqBb]/;my$re_encoded_text=qr/[^\?]*/;my$re_encoded_word=qr/=\?$re_charset(?:\*$re_language)?\?$re_encoding\?$re_encoded_text\?=/;my$re_capture_encoded_word=qr/=\?($re_charset)((?:\*$re_language)?)\?($re_encoding\?$re_encoded_text)\?=/;my$re_capture_encoded_word_split=qr/=\?($re_charset)((?:\*$re_language)?)\?($re_encoding)\?($re_encoded_text)\?=/;my$re_encoding_strict_b=qr/[Bb]/;my$re_encoding_strict_q=qr/[Qq]/;my$re_encoded_text_strict_b=qr/(?:[0-9A-Za-z\+\/]{4})*(?:[0-9A-Za-z\+\/]{2}==|[0-9A-Za-z\+\/]{3}=|)/;my$re_encoded_text_strict_q=qr/(?:[\x21-\x3C\x3E\x40-\x7E]|=[0-9A-Fa-f]{2})*/;my$re_encoded_word_strict=qr/=\?$re_charset(?:\*$re_language)?\?(?:$re_encoding_strict_b\?$re_encoded_text_strict_b|$re_encoding_strict_q\?$re_encoded_text_strict_q)\?=/;my$re_capture_encoded_word_strict=qr/=\?($re_charset)((?:\*$re_language)?)\?($re_encoding_strict_b\?$re_encoded_text_strict_b|$re_encoding_strict_q\?$re_encoded_text_strict_q)\?=/;my$re_newline=qr/(?:\r\n|[\r\n])/;my$re_word_begin_strict=qr/(?:(?:[ \t]|\A)\(?|(?:[^\\]|\A)\)\()/;my$re_word_sep_strict=qr/(?:$re_newline?[ \t])+/;my$re_word_end_strict=qr/(?:\)\(|\)?(?:$re_newline?[ \t]|\z))/;my$re_match=qr/()((?:$re_encoded_word\s*)*$re_encoded_word)()/;my$re_match_strict=qr/($re_word_begin_strict)((?:$re_encoded_word_strict$re_word_sep_strict)*$re_encoded_word_strict)(?=$re_word_end_strict)/;my$re_capture=qr/$re_capture_encoded_word(?:\s*)?/;my$re_capture_strict=qr/$re_capture_encoded_word_strict$re_word_sep_strict?/;our$STRICT_DECODE=0;sub decode($$;$) {my ($obj,$str,$chk)=@_;return undef unless defined$str;my$re_match_decode=$STRICT_DECODE ? $re_match_strict : $re_match;my$re_capture_decode=$STRICT_DECODE ? $re_capture_strict : $re_capture;my$stop=0;my$output=substr($str,0,0);1 while not $stop and $str =~ s{^((?:[^\r\n]*(?:$re_newline[ \t])?)*)($re_newline)?}{

        my $line = $1;
        my $sep = defined $2 ? $2 : '';

        $stop = 1 unless length($line) or length($sep);

        # in non strict mode append missing '=' padding characters for b words
        # fixes below concatenation of consecutive encoded mime words
        1 while not $STRICT_DECODE and $line =~ s/(=\?$re_charset(?:\*$re_language)?\?[Bb]\?)((?:[^\?]{4})*[^\?]{1,3})(\?=)/$1.$2.('='x(4-length($2)%4)).$3/se;

        # NOTE: this code partially could break $chk support
        # in non strict mode concat consecutive encoded mime words with same charset, language and encoding
        # fixes breaking inside multi-byte characters
        1 while not $STRICT_DECODE and $line =~ s/$re_capture_encoded_word_split\s*=\?\1\2\?\3\?($re_encoded_text)\?=/=\?$1$2\?$3\?$4$5\?=/so;

        # process sequence of encoded MIME words at once
        1 while not $stop and $line =~ s{^(.*?)$re_match_decode}{

            my $begin = $1 . $2;
            my $words = $3;

            $begin =~ tr/\r\n//d;
            $output .= $begin;

            # decode one MIME word
            1 while not $stop and $words =~ s{^(.*?)($re_capture_decode)}{

                $output .= $1;
                my $orig = $2;
                my $charset = $3;
                my ($mime_enc, $text) = split /\?/, $5;

                $text =~ tr/\r\n//d;

                my $enc = Encode::find_mime_encoding($charset);

                # in non strict mode allow also perl encoding aliases
                if ( not defined $enc and not $STRICT_DECODE ) {
                    # make sure that decoded string will be always strict UTF-8
                    $charset = 'UTF-8' if lc($charset) eq 'utf8';
                    $enc = Encode::find_encoding($charset);
                }

                if ( not defined $enc ) {
                    Carp::croak qq(Unknown charset "$charset") if not ref $chk and $chk and $chk & Encode::DIE_ON_ERR;
                    Carp::carp qq(Unknown charset "$charset") if not ref $chk and $chk and $chk & Encode::WARN_ON_ERR;
                    $stop = 1 if not ref $chk and $chk and $chk & Encode::RETURN_ON_ERR;
                    $output .= ($output =~ /(?:\A|[ \t])$/ ? '' : ' ') . $orig unless $stop; # $orig mime word is separated by whitespace
                    $stop ? $orig : '';
                } else {
                    if ( uc($mime_enc) eq 'B' and $obj->{decode_b} ) {
                        my $decoded = _decode_b($enc, $text, $chk);
                        $stop = 1 if not defined $decoded and not ref $chk and $chk and $chk & Encode::RETURN_ON_ERR;
                        $output .= (defined $decoded ? $decoded : $text) unless $stop;
                        $stop ? $orig : '';
                    } elsif ( uc($mime_enc) eq 'Q' and $obj->{decode_q} ) {
                        my $decoded = _decode_q($enc, $text, $chk);
                        $stop = 1 if not defined $decoded and not ref $chk and $chk and $chk & Encode::RETURN_ON_ERR;
                        $output .= (defined $decoded ? $decoded : $text) unless $stop;
                        $stop ? $orig : '';
                    } else {
                        Carp::croak qq(MIME "$mime_enc" unsupported) if not ref $chk and $chk and $chk & Encode::DIE_ON_ERR;
                        Carp::carp qq(MIME "$mime_enc" unsupported) if not ref $chk and $chk and $chk & Encode::WARN_ON_ERR;
                        $stop = 1 if not ref $chk and $chk and $chk & Encode::RETURN_ON_ERR;
                        $output .= ($output =~ /(?:\A|[ \t])$/ ? '' : ' ') . $orig unless $stop; # $orig mime word is separated by whitespace
                        $stop ? $orig : '';
                    }
                }

            }se;

            if ( not $stop ) {
                $output .= $words;
                $words = '';
            }

            $words;

        }se;

        if ( not $stop ) {
            $line =~ tr/\r\n//d;
            $output .= $line . $sep;
            $line = '';
            $sep = '';
        }

        $line . $sep;

    }se;$_[1]=$str if not ref$chk and $chk and!($chk & Encode::LEAVE_SRC);return$output}sub _decode_b {my ($enc,$text,$chk)=@_;my$octets=$STRICT_DECODE ? MIME::Base64::decode($text): join('',map {MIME::Base64::decode($_)}split /(?<==)(?=[^=])/,$text);return _decode_octets($enc,$octets,$chk)}sub _decode_q {my ($enc,$text,$chk)=@_;$text =~ s/_/ /go;$text =~ s/=([0-9A-Fa-f]{2})/pack('C', hex($1))/ego;return _decode_octets($enc,$text,$chk)}sub _decode_octets {my ($enc,$octets,$chk)=@_;$chk=0 unless defined$chk;$chk &= ~Encode::LEAVE_SRC if not ref$chk and $chk;my$output=$enc->decode($octets,$chk);return undef if not ref$chk and $chk and $octets ne '';return$output}sub encode($$;$) {my ($obj,$str,$chk)=@_;return undef unless defined$str;my$output=$obj->_fold_line($obj->_encode_string($str,$chk));$_[1]=$str if not ref$chk and $chk and!($chk & Encode::LEAVE_SRC);return$output .substr($str,0,0)}sub _fold_line {my ($obj,$line)=@_;my$bpl=$obj->{bpl};my$output='';while (length($line)){if ($line =~ s/^(.{0,$bpl})(\s|\z)//){$output .= $1;$output .= "\r\n" .$2 if length($line)}elsif ($line =~ s/(\s)(.*)$//){$output .= $line;$line=$2;$output .= "\r\n" .$1 if length($line)}else {$output .= $line;last}}return$output}sub _encode_string {my ($obj,$str,$chk)=@_;my$wordlen=$obj->{bpl}> 76 ? 76 : $obj->{bpl};my$enc=Encode::find_mime_encoding($obj->{charset});my$enc_chk=$chk;$enc_chk=0 unless defined$enc_chk;$enc_chk |= Encode::LEAVE_SRC if not ref$enc_chk and $enc_chk;my@result=();my$octets='';while (length(my$chr=substr($str,0,1,''))){my$seq=$enc->encode($chr,$enc_chk);if (not length($seq)){substr($str,0,0,$chr);last}if ($obj->_encoded_word_len($octets .$seq)> $wordlen){push@result,$obj->_encode_word($octets);$octets=''}$octets .= $seq}length($octets)and push@result,$obj->_encode_word($octets);$_[1]=$str if not ref$chk and $chk and!($chk & Encode::LEAVE_SRC);return join(' ',@result)}sub _encode_word {my ($obj,$octets)=@_;my$charset=$obj->{charset};my$encode=$obj->{encode};my$text=$encode eq 'B' ? _encode_b($octets): _encode_q($octets);return "=?$charset?$encode?$text?="}sub _encoded_word_len {my ($obj,$octets)=@_;my$charset=$obj->{charset};my$encode=$obj->{encode};my$text_len=$encode eq 'B' ? _encoded_b_len($octets): _encoded_q_len($octets);return length("=?$charset?$encode??=")+ $text_len}sub _encode_b {my ($octets)=@_;return MIME::Base64::encode($octets,'')}sub _encoded_b_len {my ($octets)=@_;return (length($octets)+ 2)/ 3 * 4}my$re_invalid_q_char=qr/[^0-9A-Za-z !*+\-\/]/;sub _encode_q {my ($octets)=@_;$octets =~ s{($re_invalid_q_char)}{
        join('', map { sprintf('=%02X', $_) } unpack('C*', $1))
    }egox;$octets =~ s/ /_/go;return$octets}sub _encoded_q_len {my ($octets)=@_;my$invalid_count=()=$octets =~ /$re_invalid_q_char/sgo;return ($invalid_count * 3)+ (length($octets)- $invalid_count)}1;