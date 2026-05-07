package Image::ExifTool::LIF;use strict;use vars qw($VERSION);use Image::ExifTool qw(:DataAccess :Utils);use Image::ExifTool::XMP;$VERSION='1.01';%Image::ExifTool::LIF::Main=(GROUPS=>{0=>'XML',1=>'XML',2=>'Image' },PROCESS_PROC=>\&Image::ExifTool::XMP::ProcessXMP,VARS=>{NO_ID=>1 },NOTES=>q{
        Tags extracted from Leica Image Format (LIF) imaging files.  As well as the
        tags listed below, all available information is extracted from the
        XML-format metadata in the LIF header.
    },TimeStampList=>{Groups=>{2=>'Time' },ValueConv=>q{
            my $unixTimeZero = 134774 * 24 * 3600;
            my @vals = split ' ', $val;
            foreach (@vals) {
                if (/[^0-9a-f]/i) {
                    $_ = '0000:00:00 00:00:00';
                } elsif (length $_ > 8) {
                    my $lo = hex substr($_, -8);
                    my $hi = hex substr($_, 0, -8);
                    $_ = 1e-7 * ($hi * 4294967296 + $lo);
                } else {
                    $_ = 1e-7 * hex($_);
                }
                # shift from Jan 1, 1601 to Jan 1, 1970
                $_ = Image::ExifTool::ConvertUnixTime($_ - $unixTimeZero);
            }
            return \@vals;
        },},);sub ShortenTagNames($) {local $_;$_=shift;s/DescriptionDimensionsDimensionDescription/Dimensions/;s/DescriptionChannelsChannelDescription/Channel/;s/ShutterListShutter/Shutter/;s/SettingDefinition/Setting/;s/AdditionalZPositionListAdditionalZPosition/AdditionalZPosition/;s/LMSDataContainerHeader//g;s/FilterWheelWheel/FilterWheel/;s/FilterWheelFilter/FilterWheel/;s/DetectorListDetector/Detector/;s/OnlineDyeSeparationOnlineDyeSeparation/OnlineDyeSeparation/;s/AotfListAotf/Aotf/;s/SettingAotfLaserLineSetting/SettingAotfLaser/;s/DataROISetROISet/DataROISet/;s/AdditionalZPosition/AddZPos/;s/FRAPplusBlock_FRAPBlock_FRAP_PrePost_Info/FRAP_/;s/FRAPplusBlock_FRAPBlock_FRAP_(Master)?/FRAP_/;s/LDM_Block_SequentialLDM_Block_Sequential_/LDM_/;s/ATLConfocalSetting/ATLConfocal/;s/LaserArrayLaser/Laser/;s/LDM_Master/LDM_/;s/(List)?ATLConfocal/ATL_/;s/Separation/Sep/;s/BleachPointsElement/BleachPoint/;s/BeamPositionBeamPosition/BeamPosition/;s/DataROISetPossible(ROI)?/DataROISet/;s/RoiElementChildrenElementDataROISingle(Roi)?/Roi/;s/InfoLaserLineSettingArrayLaserLineSetting/LastLineSetting/;s/FilterWheelWheelNameFilterName/FilterWheelFilterName/;s/LUT_ListLut/Lut/;s/ROI_ListRoiRoidata/ROI_/;s/LaserLineSettingArrayLaserLineSetting/LaserLineSetting/;return $_}sub ProcessLIF($$) {my ($et,$dirInfo)=@_;my$raf=$$dirInfo{RAF};my$buff;return 0 unless$raf->Read($buff,15)==15 and $buff =~ /^\x70\0{3}.{4}\x2a.{4}<\0/s;$et->SetFileType();SetByteOrder('II');my$size=Get32u(\$buff,4);my$len=Get32u(\$buff,9)* 2;$size < $len and $et->Error('Corrupted LIF XML block'),return 1;$size > 100000000 and $et->Error('LIF XML block too large'),return 1;$raf->Seek(-2,1)and $raf->Read($buff,$len)==$len or $et->Error('Truncated LIF XML block'),return 1;my$tagTablePtr=GetTagTable('Image::ExifTool::LIF::Main');my$xml=Image::ExifTool::Decode($et,$buff,'UCS2','II','UTF8');my%dirInfo=(DataPt=>\$xml);$$et{XmpIgnoreProps}=['LMSDataContainerHeader','Element','Children','Data','Image','Attachment' ];$$et{ShortenXmpTags}=\&ShortenTagNames;$et->ProcessDirectory(\%dirInfo,$tagTablePtr);return 1}1;