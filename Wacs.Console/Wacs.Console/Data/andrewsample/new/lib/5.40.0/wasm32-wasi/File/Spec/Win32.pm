package File::Spec::Win32;use strict;use Cwd ();require File::Spec::Unix;our$VERSION='3.91';$VERSION =~ tr/_//d;our@ISA=qw(File::Spec::Unix);my$DRIVE_RX='[a-zA-Z]:';my$UNC_RX='(?:\\\\\\\\|//)[^\\\\/]+[\\\\/][^\\\\/]+';my$VOL_RX="(?:$DRIVE_RX|$UNC_RX)";sub devnull {return "nul"}sub rootdir {'\\'}sub tmpdir {my$tmpdir=$_[0]->_cached_tmpdir(qw(TMPDIR TEMP TMP));return$tmpdir if defined$tmpdir;$tmpdir=$_[0]->_tmpdir(map($ENV{$_},qw(TMPDIR TEMP TMP)),'SYS:/temp','C:\system\temp','C:/temp','/tmp','/');$_[0]->_cache_tmpdir($tmpdir,qw(TMPDIR TEMP TMP))}sub case_tolerant {eval {local@INC=@INC;pop@INC if$INC[-1]eq '.';require Win32API::File}or return 1;my$drive=shift || "C:";my$osFsType="\0"x256;my$osVolName="\0"x256;my$ouFsFlags=0;Win32API::File::GetVolumeInformation($drive,$osVolName,256,[],[],$ouFsFlags,$osFsType,256);if ($ouFsFlags & Win32API::File::FS_CASE_SENSITIVE()){return 0}else {return 1}}sub file_name_is_absolute {my ($self,$file)=@_;if ($file =~ m{^($VOL_RX)}o){my$vol=$1;return ($vol =~ m{^$UNC_RX}o ? 2 : $file =~ m{^$DRIVE_RX[\\/]}o ? 2 : 0)}return$file =~ m{^[\\/]} ? 1 : 0}sub catfile {shift;shift,return _canon_cat("/",@_)if!@_ || $_[0]eq "";return _canon_cat(($_[0].'\\'),@_[1..$#_])if $_[0]=~ m{^$DRIVE_RX\z}o;return _canon_cat(@_)}sub catdir {shift;return "" unless @_;shift,return _canon_cat("/",@_)if $_[0]eq "";return _canon_cat(($_[0].'\\'),@_[1..$#_])if $_[0]=~ m{^$DRIVE_RX\z}o;return _canon_cat(@_)}sub path {my@path=split(';',$ENV{PATH});s/"//g for@path;@path=grep length,@path;unshift(@path,".");return@path}sub canonpath {return $_[1]if!defined($_[1])or $_[1]eq '';return _canon_cat($_[1])}sub splitpath {my ($self,$path,$nofile)=@_;my ($volume,$directory,$file)=('','','');if ($nofile){$path =~ m{^ ( $VOL_RX ? ) (.*) }sox;$volume=$1;$directory=$2}else {$path =~ m{^ ( $VOL_RX ? )
                ( (?:.*[\\/](?:\.\.?\Z(?!\n))?)? )
                (.*)
             }sox;$volume=$1;$directory=$2;$file=$3}return ($volume,$directory,$file)}sub splitdir {my ($self,$directories)=@_ ;if ($directories !~ m|[\\/]\Z(?!\n)|){return split(m|[\\/]|,$directories)}else {my(@directories)=split(m|[\\/]|,"${directories}dummy");$directories[$#directories ]='' ;return@directories }}sub catpath {my ($self,$volume,$directory,$file)=@_;my$v;$volume .= $v if ((($v)=$volume =~ m@^([\\/])[\\/][^\\/]+[\\/][^\\/]+\Z(?!\n)@s)&& $directory =~ m@^[^\\/]@s);$volume .= $directory ;if ($volume !~ m@^[a-zA-Z]:\Z(?!\n)@s && $volume =~ m@[^\\/]\Z(?!\n)@ && $file =~ m@[^\\/]@){$volume =~ m@([\\/])@ ;my$sep=$1 ? $1 : '\\' ;$volume .= $sep }$volume .= $file ;return$volume }sub _same {lc($_[1])eq lc($_[2])}sub rel2abs {my ($self,$path,$base)=@_;my$is_abs=$self->file_name_is_absolute($path);return$self->canonpath($path)if$is_abs==2;if ($is_abs){my$vol=($self->splitpath(Cwd::getcwd()))[0];return$self->canonpath($vol .$path)}if (!defined($base)|| $base eq ''){$base=Cwd::getdcwd(($self->splitpath($path))[0])if defined&Cwd::getdcwd ;$base=Cwd::getcwd()unless defined$base }elsif (!$self->file_name_is_absolute($base)){$base=$self->rel2abs($base)}else {$base=$self->canonpath($base)}my ($path_directories,$path_file)=($self->splitpath($path,1))[1,2];my ($base_volume,$base_directories)=$self->splitpath($base,1);$path=$self->catpath($base_volume,$self->catdir($base_directories,$path_directories),$path_file);return$self->canonpath($path)}sub _canon_cat {my ($first,@rest)=@_;my$volume=$first =~ s{ \A ([A-Za-z]:) ([\\/]?) }{}x ? ucfirst($1).($2 ? "\\" : ""): $first =~ s{ \A (?:\\\\|//) ([^\\/]+)
				 (?: [\\/] ([^\\/]+) )?
	       			 [\\/]? }{}xs ? "\\\\$1".(defined $2 ? "\\$2" : "")."\\" : $first =~ s{ \A [\\/] }{}x ? "\\" : "";my$path=join "\\",$first,@rest;$path =~ tr#\\/#\\\\#s;$path =~ s{(?:
		(?:\A|\\)		# at begin or after a slash
		\.
		(?:\\\.)*		# and more
		(?:\\|\z) 		# at end or followed by slash
	       )+			# performance boost -- I do not know why
	     }{\\}gx;while ($path =~ s{(?:
		(?:\A|\\)		# at begin or after a slash
		[^\\]+			# rip this 'yy' off
		\\\.\.
		(?<!\A\.\.\\\.\.)	# do *not* replace ^..\..
		(?<!\\\.\.\\\.\.)	# do *not* replace \..\..
		(?:\\|\z) 		# at end or followed by slash
	       )+			# performance boost -- I do not know why
	     }{\\}sx){}$path =~ s#\A\\##;$path =~ s#\\\z##;if ($volume =~ m#\\\z#){$path =~ s{ \A			# at begin
		    \.\.
		    (?:\\\.\.)*		# and more
		    (?:\\|\z) 		# at end or followed by slash
		 }{}x;return $1 if$path eq "" and $volume =~ m#\A(\\\\.*)\\\z#s}return$path ne "" || $volume ? $volume.$path : "."}1;