package O;our$VERSION='1.03';use B ();our$BEGIN_output;our$saveout_fh;sub import {my ($class,@options)=@_;my ($quiet,$veryquiet)=(0,0);if ($options[0]eq '-q' || $options[0]eq '-qq'){$quiet=1;open ($saveout_fh,">&",STDOUT);close STDOUT;open (STDOUT,">",\$O::BEGIN_output);if ($options[0]eq '-qq'){$veryquiet=1}shift@options}my$backend=shift (@options);eval q[
	BEGIN {
	    B::minus_c;
	    B::save_BEGINs;
	}

	CHECK {
	    if ($quiet) {
		close STDOUT;
		open (STDOUT, ">&", $saveout_fh);
		close $saveout_fh;
	    }

	    # Note: if you change the code after this 'use', please
	    # change the fudge factors in B::Concise (grep for
	    # "fragile kludge") so that its output still looks
	    # nice. Thanks. --smcc
	    use B::].$backend.q[ ();

	    my $compilesub = &{"B::${backend}::compile"}(@options);
	    if (ref($compilesub) ne "CODE") {
		die $compilesub;
	    }

	    local $savebackslash = $\;
	    local ($\,$",$,) = (undef,' ','');
	    &$compilesub();

	    close STDERR if $veryquiet;
	}
    ];if ($@){my$msg="$@";require Carp;Carp::croak("Loading compiler backend 'B::$backend' failed: $msg")}}1;