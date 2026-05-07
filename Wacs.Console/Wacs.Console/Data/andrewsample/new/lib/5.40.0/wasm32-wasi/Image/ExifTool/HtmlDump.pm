package Image::ExifTool::HtmlDump;use strict;use vars qw($VERSION);use Image::ExifTool;use Image::ExifTool::HTML qw(EscapeHTML);$VERSION='1.42';sub DumpTable($$$;$$$$$$);sub Open($$$;@);sub Write($@);my ($bkgStart,$bkgEnd,@bkgSpan);my$htmlHeader1=<<_END_PART_1_;my$htmlHeader2=<<_END_PART_2_;my$htmlHeader3=q[
<!DOCTYPE HTML PUBLIC "-//W3C//DTD HTML 4.0 Transitional//EN"
 "http://www.w3.org/TR/1998/REC-html40-19980424/loose.dtd">
<html>
<head>
<title>
_END_PART_1_
</title>
<meta http-equiv="Content-Type" content="text/html; charset=UTF-8">
<style type="text/css">
<!--
/* character style ID's */
.D { color: #000000 } /* default color */
.V { color: #ff0000 } /* duplicate block 1 */
.W { color: #004400 } /* normal block 1 */
.X { color: #ff4488 } /* duplicate block 2 */
.Y { color: #448844 } /* normal block 2 */
.U { color: #cc8844 } /* unused data block */
.H { color: #0000ff } /* highlighted tag name */
.F { color: #aa00dd } /* actual offset differs */
.M { text-decoration: underline } /* maker notes data */
.tt { /* tooltip text */
    visibility: hidden;
    position: absolute;
    white-space: nowrap;
    top: 0;
    left: 0;
    font-family: Verdana, sans-serif;
    font-size: .7em;
    padding: 2px 4px;
    border: 1px solid gray;
    z-index: 3;
}
.tb { /* tooltip background */
    visibility: hidden;
    position: absolute;
    background: #ffffdd;
    zoom: 1;
    -moz-opacity: 0.8;
    -khtml-opacity: 0.8;
    -ms-filter: 'progid:DXImageTransform.Microsoft.Alpha(Opacity=80)';
    filter: alpha(opacity=80);
    opacity: 0.8;
    z-index: 2;
}
/* table styles */
table.dump {
  border-top: 1px solid gray;
  border-bottom: 1px solid gray;
}
table.dump td { padding: .2em .3em }
td.c2 {
  border-left: 1px solid gray;
  border-right: 1px solid gray;
}
pre   { margin: 0 }
table { font-size: .9em }
body  { color: black; background: white }
-->
</style>
<script language="JavaScript" type="text/JavaScript">
<!-- Begin
// tooltip positioning constants
var TMAR = 4;   // top/left margins
var BMAR = 16;  // bottom/right margins (scrollbars may overhang inner dimensions)
var XOFF = 10;  // x offset from cursor
var YOFF = 40;  // y offset
var YMIN = 10;  // minimum y offset
var YTOP = 20;  // y offset when above cursor
// common variables
var safari1 = navigator.userAgent.indexOf("Safari/312.6") >= 0;
var ie6 = navigator.userAgent.toLowerCase().indexOf('msie 6') >= 0;
var mspan = new Array;
var clicked = 0;
var hlist, tt, tb, firstOutEvt, lastInEvt;

function GetElementsByClass(classname, tagname) {
  var found = new Array();
  var list = document.getElementsByTagName(tagname);
  var len = list.length;
  for (var i=0, j=0; i<len; ++i) {
    var classes = list[i].className.split(' ');
    for (var k=0; k<classes.length; ++k) {
      if (classes[k] == classname) {
        found[j++] = list[i];
        break;
      }
    }
  }
  return found;
}

// click mouse
function doClick(e)
{
  if (!clicked) {
    firstOutEvt = lastInEvt = undefined;
    high(e, 2);
    if (hlist) clicked = 1;
  } else {
    clicked = 0;
    if (firstOutEvt) high(firstOutEvt, 0);
    if (lastInEvt) high(lastInEvt, 1);
  }
}

// move tooltip
function move(e)
{
  if (!tt) return;
  if (ie6 && (tt.style.top  == '' || tt.style.top  == 0) &&
             (tt.style.left == '' || tt.style.left == 0))
  {
    tt.style.width  = tt.offsetWidth  + 'px';
    tt.style.height = tt.offsetHeight + 'px';
  }
  var w, h;
  // browser inconsistencies make getting window size more complex than it should be,
  // and even then we don't know if it is smaller due to scrollbar width
  if (typeof(window.innerWidth) == 'number') {
    w = window.innerWidth;
    h = window.innerHeight;
  } else if (document.documentElement && document.documentElement.clientWidth) {
    w = document.documentElement.clientWidth;
    h = document.documentElement.clientHeight;
  } else {
    w = document.body.clientWidth;
    h = document.body.clientHeight;
  }
  var x = e.clientX + XOFF;
  var y = e.clientY + YOFF;
  if (safari1) { // patch for people still using OS X 10.3.9
    x -= document.body.scrollLeft + document.documentElement.scrollLeft;
    y -= document.body.scrollTop  + document.documentElement.scrollTop;
  }
  var mx = w - BMAR - tt.offsetWidth;
  var my = h - BMAR - tt.offsetHeight;
  if (y > my + YOFF - YMIN) y = e.clientY - YTOP - tt.offsetHeight;
  if (x > mx) x = mx;
  if (y > my) y = my;
  if (x < TMAR) x = TMAR;
  if (y < TMAR) y = TMAR;
  x += document.body.scrollLeft + document.documentElement.scrollLeft;
  y += document.body.scrollTop  + document.documentElement.scrollTop;
  tb.style.width  = tt.offsetWidth  + 'px';
  tb.style.height = tt.offsetHeight + 'px';
  tt.style.top  = tb.style.top  = y + 'px';
  tt.style.left = tb.style.left = x + 'px';
  tt.style.visibility = tb.style.visibility = 'visible';
}

// highlight/unhighlight text
function high(e,on) {
  if (on) {
    lastInEvt = e;
  } else {
    if (!firstOutEvt) firstOutEvt = e;
  }
  if (clicked) return;
  var targ;
  if (e.target) targ = e.target;
  else if (e.srcElement) targ = e.srcElement;
  if (targ.nodeType == 3) targ = targ.parentNode; // defeat Safari bug
  if (!targ.name) targ = targ.parentNode; // go up another level if necessary
  if (targ.name && document.getElementsByName) {
    // un-highlight current objects
    if (hlist) {
      for (var i=0; i<hlist.length; ++i) {
        for (var j=0; j<hlist[i].length; ++j) {
          hlist[i][j].style.background = 'transparent';
        }
      }
      hlist = null;
    }
    if (tt) {
      // hide old tooltip
      tt.style.visibility = tb.style.visibility = 'hidden';
      tt = null;
    }
    if (on) {
      if (targ.name.substring(0,1) == 't') {
        // show our tooltip (ID is different than name to avoid confusing IE)
        tt = document.getElementById('p' + targ.name.substring(1));
        if (tt) {
          tb = document.getElementById('tb');
          move(e);
        }
      }
      // highlight anchor elements with the same name
      hlist = new Array;
      hlist.push(document.getElementsByName(targ.name));
      // is this an IFD pointer?
      var pos = targ.className.indexOf('Offset_');
      if (pos > 0) {
        // add elements from this IFD to our highlight list
        hlist.push(document.getElementsByClassName(targ.className.substr(pos+7)));
      }
      // use class name to highlight span elements if necessary
      for (var i=0; i<mspan.length; ++i) {
        if (mspan[i] != targ.name) continue;
        // add these span elements to our highlight list
        hlist.push(GetElementsByClass(targ.name, 'span'));
        break;
      }
      for (var i=0; i<hlist.length; ++i) {
        for (var j=0; j<hlist[i].length; ++j) {
          hlist[i][j].style.background = on == 2 ? '#ffbbbb' : '#ffcc99';
        }
      }
    }
  }
}
_END_PART_2_
// End --->
</script></head>
<body><noscript><b class=V>--&gt;
Enable JavaScript for active highlighting and information tool tips!
</b></noscript>
<table class=dump cellspacing=0 cellpadding=2>
<tr><td valign='top'><pre>];my$preMouse=q(<pre onmouseover="high(event,1)" onmouseout="high(event,0)" onmousemove="move(event)" onmousedown="doClick(event)">);sub new {local $_;my$that=shift;my$class=ref($that)|| $that || 'Image::ExifTool::HtmlDump';return bless {Block=>{},TipNum=>0 },$class}sub Add($$$$;$$) {my ($self,$start,$size,$msg,$tip,$flag,$ifd)=@_;my$block=$$self{Block};$$block{$start}or $$block{$start}=[];my$htip;if ($tip and $tip eq 'SAME'){$htip=''}else {$htip=($msg =~ /^[[(]/)? $msg : "<b>$msg</b>";if (defined$tip){($tip=EscapeHTML($tip))=~ s/\n/<br>/g;$htip .= '<br>' .$tip}$htip .= "<br>($size bytes)" unless$htip =~ /<br>Size:/;++$self->{TipNum}}push @{$$block{$start}},[$size,$msg,$htip,$flag,$self->{TipNum},$ifd ]}sub Print($$;$$$$$) {local $_;my ($self,$raf,$dataPt,$dataPos,$outfile,$level,$title)=@_;my ($i,$buff,$rtnVal,$limit,$err);my$block=$$self{Block};$dataPos=0 unless$dataPos;$outfile=\*STDOUT unless ref$outfile;$title='HtmlDump' unless$title;$level or $level=0;my$tell=$raf->Tell();$raf->Seek(0,2)or $$self{ERROR}='Seek error',return -1;my$fileLen=$raf->Tell();my$pos=0;my$dataEnd=$dataPos + ($dataPt ? length($$dataPt): 0);$$self{Open}=[];$$self{Closed}=[];$$self{TipList}=[];$$self{MSpanList}=[];$$self{Cols}=['','','','' ];if ($level <= 1){$limit=1024}elsif ($level <= 2){$limit=16384}else {$limit=256 * 1024 * 1024}$$self{Limit}=$limit;for ($i=0;$i<4;++$i){$self->{Open}->[$i]={ID=>[],Element=>{}};$self->{Closed}->[$i]={ID=>[],Element=>{}}}$bkgStart=$bkgEnd=0;undef@bkgSpan;my$index=0;my (@names,$wasUnused,@starts);@starts=sort {$a <=> $b}keys %$block unless $$self{Error};for ($i=0;$i<=@starts;++$i){my$start=$starts[$i];my$parmList;if (defined$start){$parmList=$$block{$start}}elsif ($bkgEnd and $pos < $bkgEnd and not defined$wasUnused){$start=$bkgEnd}else {last}$start=$fileLen if$start > $fileLen;my$len=$start - $pos;if ($len > 0 and not $wasUnused){--$i;my ($nextBkgEnd,$bkg);if (not defined$wasUnused and $bkgEnd){for$bkg (@bkgSpan){next if$pos >= $$bkg{End}+ $dataPos or $pos + $len <= $$bkg{End}+ $dataPos;$nextBkgEnd=$$bkg{End}unless$nextBkgEnd and $nextBkgEnd < $$bkg{End}}}if ($nextBkgEnd){$start=$pos;$len=$nextBkgEnd + $dataPos - $pos;$wasUnused=0}else {$start=$pos;$wasUnused=1}my$str=($len > 1)? "unused $len bytes" : 'pad byte';$parmList=[[$len,"[$str]",undef,0x108 ]]}else {undef$wasUnused}my$parms;for$parms (@$parmList){my ($len,$msg,$tip,$flag,$tipNum,$ifd)=@$parms;next unless$len > 0;$flag=0 unless defined$flag;my$name;$name=$names[$tipNum]if defined$tipNum;my$idx=$index;if ($name){$idx=substr($name,1)}else {$name="t$index";$names[$tipNum]=$name if defined$tipNum;++$index}if ($flag & 0x14){my$class=$flag & 0x04 ? "$name M" : $name;$class .= " $ifd" if$ifd;my%bkg=(Class=>$class,Start=>$start - $dataPos,End=>$start - $dataPos + $len,);push@bkgSpan,\%bkg;$bkgStart=$bkg{Start}unless$bkgStart and $bkgStart < $bkg{Start};$bkgEnd=$bkg{End}unless$bkgEnd and $bkgEnd > $bkg{End};push @{$self->{MSpanList}},$name;next}my ($end,$try);for ($try=0;$try<2;++$try){$end=$start + $len;my$size=($len > $limit + 32)? $limit / 2 + 16 : $len;if ($start >= $dataPos and $end <= $dataEnd){$buff=substr($$dataPt,$start-$dataPos,$size);if ($len!=$size){$buff .= substr($$dataPt,$start-$dataPos+$len-$size,$size)}}else {$buff='';if ($raf->Seek($start,0)and $raf->Read($buff,$size)==$size){if ($len!=$size){my$buf2='';unless ($raf->Seek($start+$len-$size,0)and $raf->Read($buf2,$size)==$size){$err=$msg;$len=$fileLen - $start;$tip .= "<br>Error: Only $len bytes available!" if$tip;next}$buff .= $buf2;undef$buf2}}else {$err=$msg;$len=length$buff;$tip .= "<br>Error: Only $len bytes available!" if$tip}}last}$tip and $self->{TipList}->[$idx]=$tip;next unless length$buff;if ($i+1 < @starts and $parms eq $$parmList[-1]and ($end==$starts[$i+1]or ($end < $starts[$i+1]and $end >= $pos))){my$nextFlag=$block->{$starts[$i+1]}->[0]->[3]|| 0;$flag |= 0x100 unless$flag & 0x01 or $nextFlag & 0x01}$self->DumpTable($start-$dataPos,\$buff,$msg,$name,$flag,$len,$pos-$dataPos,$ifd);undef$buff;$pos=$end if$pos < $end}}$self->Open('','');$raf->Seek($tell,0);Write($outfile,$htmlHeader1,$title);if ($self->{Cols}->[0]){Write($outfile,$htmlHeader2);my$mspan=\@{$$self{MSpanList}};for ($i=0;$i<@$mspan;++$i){Write($outfile,qq(mspan[$i] = "$$mspan[$i]";\n))}Write($outfile,$htmlHeader3,$self->{Cols}->[0]);Write($outfile,'</pre></td><td valign="top">',$preMouse,$self->{Cols}->[1]);Write($outfile,'</pre></td><td class=c2 valign="top">',$preMouse,$self->{Cols}->[2]);Write($outfile,'</pre></td><td valign="top">',$preMouse,$self->{Cols}->[3]);Write($outfile,"</pre></td></tr></table>\n<div id=tb class=tb> </div>\n");my$tips=\@{$$self{TipList}};for ($i=0;$i<@$tips;++$i){my$tip=$$tips[$i];Write($outfile,"<div id=p$i class=tt>$tip</div>\n")if defined$tip}delete $$self{TipList};$rtnVal=1}else {my$err=$$self{Error}|| 'No EXIF or TIFF information found in image';Write($outfile,"$title</title></head><body>\n$err\n");$rtnVal=0}Write($outfile,"</body></html>\n");for ($i=0;$i<4;++$i){$self->{Cols}->[$i]=''}if ($err){$err =~ tr/()//d;$$self{ERROR}=$err;return -1}return$rtnVal}sub Open($$$;@) {my ($self,$id,$element,@colNums)=@_;@colNums or @colNums=(0 .. $#{$self->{Open}});my$col;for$col (@colNums){my$opHash=$self->{Open}->[$col];my$opElem=$$opHash{Element};if ($element){next if $$opElem{$id}and $$opElem{$id}eq $element}elsif ($id and not $$opElem{$id}){next unless$element eq '' and @{$self->{Closed}->[$col]->{ID}}}my$opID=$$opHash{ID};my$clHash=$self->{Closed}->[$col];my$clID=$$clHash{ID};my$clElem=$$clHash{Element};my$cols=$$self{TmpCols}|| $$self{Cols};if ($$opElem{$id}or not $id){while (@$opID){my$tid=pop @$opID;my$e=$$opElem{$tid};$e =~ s/^<(\S+).*/<\/$1>/s;$$cols[$col].= $e;if ($id eq $tid or not $id){delete $$opElem{$tid};last if$id;next}push @$clID,$tid;$$clElem{$tid}=$$opElem{$tid};delete $$opElem{$tid}}unless ($id){$clID=$$clHash{ID}=[];$clElem=$$clHash{Element}={}}}elsif ($$clElem{$id}){delete $$clElem{$id};@$clID=grep!/^$id$/,@$clID}next if$element eq '0';while (@$clID){my$tid=pop @$clID;$$cols[$col].= $$clElem{$tid};push @$opID,$tid;$$opElem{$tid}=$$clElem{$tid};delete $$clElem{$tid}}if ($element and $element ne '1'){$$cols[$col].= $element;push @$opID,$id;$$opElem{$id}=$element}}}sub DumpTable($$$;$$$$$$) {my ($self,$pos,$blockPt,$msg,$name,$flag,$len,$endPos,$ifd)=@_;$len=length $$blockPt unless defined$len;$endPos=0 unless$endPos;my ($f0,$dblRef,$id);my$skipped=0;if (($endPos and $pos < $endPos)or $flag & 0x02){$f0="<span class=V>";$dblRef=1 if$endPos and $pos < $endPos}else {$f0=''}my@c=('','','','');$$self{TmpCols}=\@c;if ($name){if ($msg and $msg =~ /^\[/){$id='U'}else {if ($$self{A}){$id='X';$$self{A}=0}else {$id='V';$$self{A}=1}++$id unless$dblRef}my$class=$ifd ? "'$id $ifd'" : $id;$name="<a name=$name class=$class>";$msg and $msg="$name$msg</a>"}else {$name=''}my$cols=0;my$p=$pos;if ($$self{Cont}){$cols=$pos & 0x0f;$c[1].= ($cols==8)? '  ' : ' '}else {my$addr=$pos < 0 ? sprintf("-%.4x",-$pos): sprintf("%5.4x",$pos);$self->Open('fgd',$f0,0);$self->Open('fgd','',3);$c[0].= "$addr";$p -= $pos & 0x0f unless$flag & 0x01;if ($p < $pos){$self->Open('bkg','',1,2);$cols=$pos - $p;my$n=3 * $cols;++$n if$cols > 7;$c[1].= ' ' x $n;$c[2].= ' ' x $cols;$p=$pos}}for (;;){my (@spanClass,@spanCont,$spanClose,$bkg);if ($p >= $bkgStart and $p < $bkgEnd){for$bkg (@bkgSpan){next unless$p >= $$bkg{Start}and $p < $$bkg{End};push@spanClass,$$bkg{Class};if ($p + 1==$$bkg{End}){$spanClose=1}else {push@spanCont,$$bkg{Class}}}$self->Open('bkg',@spanClass ? "<span class='@spanClass'>" : '',1,2)}else {$self->Open('bkg','',1,2)}$self->Open('a',$name,1,2);my$ch=substr($$blockPt,$p-$pos-$skipped,1);$c[1].= sprintf("%.2x",ord($ch));$ch =~ tr/\x00-\x1f\x7f-\xff/./;$ch =~ s/&/&amp;/g;$ch =~ s/>/&gt;/g;$ch =~ s/</&lt;/g;$c[2].= $ch;++$p;++$cols;if ($spanClose){my$spanCont=@spanCont ? "<span class='@spanCont'>" : '';my$arg=($p - $pos >= $len)? 0 : $spanCont;$self->Open('bkg',$arg,1,2)}if ($dblRef and $p >= $endPos){$dblRef=0;++$id;my$class=$ifd ? "'$id $ifd'" : $id;$name =~ s/class=\w\b/class=$class/;$f0='';$self->Open('fgd',$f0,0)}if ($p - $pos >= $len){$self->Open('a','',1,2);last}if ($cols < 16){$c[1].= ($cols==8 ? '  ' : ' ');next}elsif ($flag & 0x01 and $cols < $len){$c[1].= ' ';next}unless ($$self{Msg}){$c[3].= $msg;$msg=''}$_ .= "\n" foreach@c;$$self{Msg}=0;if ($$self{Limit}){my$div=($flag & 0x08)? 4 : 1;my$lim=$$self{Limit}/ (2 * $div)- 16;if ($p - $pos > $lim and $len - $p + $pos > $lim){my$n=($len - $p + $pos - $lim)& ~0x0f;if ($n > 16){$self->Open('bkg','',1,2);my$note=sprintf "[snip %d lines]",$n / 16;$note=(' ' x (24-length($note)/2)).$note;$c[0].= "  ...\n";$c[1].= $note .(' ' x (48-length($note)))."\n";$c[2].= "     [snip]     \n";$c[3].= "\n";$p += $n;$skipped += $len - length $$blockPt}}}$c[0].= ($p < 0 ? sprintf("-%.4x",-$p): sprintf("%5.4x",$p));$cols=0}if ($msg){$msg=" $msg" if $$self{Msg};$c[3].= $msg}if ($flag & 0x100 and $cols < 16){$$self{Cont}=1;$$self{Msg}=1 if$msg}else {$_ .= "\n" foreach@c;$$self{Msg}=0;$$self{Cont}=0}my$i;for ($i=0;$i<4;++$i){$self->{Cols}->[$i].= $c[$i]}delete $$self{TmpCols}}sub FinishTiffDump($$$) {my ($self,$et,$size)=@_;my ($tag,$key,$start,$blockInfo,$i);my%offsetPair=(StripOffsets=>'StripByteCounts',TileOffsets=>'TileByteCounts',FreeOffsets=>'FreeByteCounts',ThumbnailOffset=>'ThumbnailLength',PreviewImageStart=>'PreviewImageLength',JpgFromRawStart=>'JpgFromRawLength',OtherImageStart=>'OtherImageLength',PreviewJXLStart=>'PreviewJXLLength',ImageOffset=>'ImageByteCount',AlphaOffset=>'AlphaByteCount',MPImageStart=>'MPImageLength',IDCPreviewStart=>'IDCPreviewLength',SamsungRawPointersOffset=>'SamsungRawPointersLength',);for$tag (keys%offsetPair){my$info=$et->GetInfo($tag);next unless %$info;if ($tag eq 'StripOffsets' and $$et{TAG_INFO}{$tag}{PanasonicHack}){my$info2=$et->GetInfo('RawDataOffset');$info2=$info unless %$info2;my@keys=keys %$info2;my$offset=$$info2{$keys[0]};my$raf=$$et{RAF};if (@keys==1 and $offset =~ /^\d+$/ and $raf){my$pos=$raf->Tell();$raf->Seek(0,2);my$len=$raf->Tell()- $offset;$raf->Seek($pos,0);if ($len > 0){$self->Add($offset,$len,"(Panasonic raw data)","Size: $len bytes",0x08);next}}}for$key (keys %$info){my$name=Image::ExifTool::GetTagName($key);my$grp1=$et->GetGroup($key,1);my$info2=$et->GetInfo($offsetPair{$tag},{Group1=>$grp1 });my$key2=$offsetPair{$tag};$key2 .= $1 if$key =~ /( .*)/;next unless $$info2{$key2};my$offsets=$$info{$key};my$byteCounts=$$info2{$key2};next if$tag eq 'MPImageStart' and $offsets eq '0';my@offsets=split ' ',(ref$offsets ? $$offsets : $offsets);my@byteCounts=split ' ',(ref$byteCounts ? $$byteCounts : $byteCounts);my$num=scalar@offsets;my$li=0;my$padBytes=0;for ($i=0;@offsets and @byteCounts;++$i){my$offset=shift@offsets;my$byteCount=shift@byteCounts;my$end=$offset + $byteCount;if (@offsets and @byteCounts){if ($end & 0x01 and $end + 1==$offsets[0]){$end += 1;++$padBytes}if ($end==$offsets[0]){$byteCounts[0]+= $offsets[0]- $offset;$offsets[0]=$offset;next}}my$msg=$et->GetGroup($key,1).':' .$tag;$msg =~ s/(Offsets?|Start)$/ /;if ($num > 1){$msg .= "$li-" if$li!=$i;$msg .= "$i ";$li=$i + 1}$msg .= "data";my$tip="Size: $byteCount bytes";$tip .= ", incl. $padBytes pad bytes" if$padBytes;$self->Add($offset,$byteCount,"($msg)",$tip,0x08)}}}my$last=0;my$block=$$self{Block};for$start (keys %$block){for$blockInfo (@{$$block{$start}}){my$end=$start + $$blockInfo[0];$last=$end if$last < $end}}my$diff=$size - $last;if ($diff > 0 and ($last or $et->Options('Unknown'))){if ($diff > 1 or $size & 0x01){$self->Add($last,$diff,"[unknown data]","Size: $diff bytes",0x08)}else {$self->Add($last,$diff,"[trailing pad byte]",undef,0x08)}}}sub Write($@) {my$outfile=shift;if (UNIVERSAL::isa($outfile,'GLOB')){return print$outfile @_}elsif (ref$outfile eq 'SCALAR'){$$outfile .= join('',@_);return 1}return 0}1;