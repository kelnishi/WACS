package Image::ExifTool::MWG;use strict;use vars qw($VERSION);use Image::ExifTool qw(:DataAccess :Utils);use Image::ExifTool::Exif;use Image::ExifTool::XMP;$VERSION='1.24';sub RecoverTruncatedIPTC($$$);sub ListToString($);sub StringToList($$);sub OverwriteStringList($$$$);my$mwgLoaded;%Image::ExifTool::MWG::Composite=(GROUPS=>{0=>'Composite',1=>'MWG',2=>'Image' },VARS=>{NO_ID=>1 },WRITE_PROC=>\&Image::ExifTool::DummyWriteProc,NOTES=>q{
        The table below lists special Composite tags which are used to access other
        tags based on the MWG 2.0 recommendations.  These tags are only accessible
        when explicitly loaded, but this is done automatically by the exiftool
        application if MWG is specified as a group for any tag on the command line,
        or manually with the C<-use MWG> option.  Via the API, the MWG Composite
        tags are loaded by calling "C<Image::ExifTool::MWG::Load()>".

        When reading, the value of each MWG tag is B<Derived From> the specified
        tags based on the MWG guidelines.  When writing, the appropriate associated
        tags are written.  The value of the IPTCDigest tag is updated automatically
        when the IPTC is changed if either the IPTCDigest tag didn't exist
        beforehand or its value agreed with the original IPTC digest (indicating
        that the XMP is synchronized with the IPTC).  IPTC information is written
        only if the original file contained IPTC.

        Loading the MWG module activates "strict MWG conformance mode", which has
        the effect of causing EXIF, IPTC and XMP in non-standard locations to be
        ignored when reading, as per the MWG recommendations.  Instead, a "Warning"
        tag is generated when non-standard metadata is encountered.  This feature
        may be disabled by setting C<$Image::ExifTool::MWG::strict = 0> in the
        L<ExifTool config file|../config.html> (or from your Perl script when using the API).  Note
        that the behaviour when writing is not changed:  ExifTool always creates new
        records only in the standard location, but writes new tags to any
        EXIF/IPTC/XMP records that exist.

        Contrary to the EXIF specification, the MWG recommends that EXIF "ASCII"
        string values be stored as UTF-8.  To honour this, the exiftool application
        sets the default internal EXIF string encoding to "UTF8" when the MWG module
        is loaded, but via the API this must be done manually by setting the
        L<CharsetEXIF|../ExifTool.html#CharsetEXIF> option.

        A complication of the MWG specification is that although the MWG:Creator
        property may consist of multiple values, the associated EXIF tag
        (EXIF:Artist) is only a simple string.  To resolve this discrepancy the MWG
        recommends a technique which allows a list of values to be stored in a
        string by using a semicolon-space separator (with quotes around values if
        necessary).  When the MWG module is loaded, ExifTool automatically
        implements this policy and changes EXIF:Artist to a list-type tag.
    },Keywords=>{Flags=>['Writable','List'],Desire=>{0=>'IPTC:Keywords',1=>'XMP-dc:Subject',2=>'CurrentIPTCDigest',3=>'IPTCDigest',},RawConv=>q{
            return $val[1] if not defined $val[2] or (defined $val[1] and
                             (not defined $val[3] or $val[2] eq $val[3]));
            return Image::ExifTool::MWG::RecoverTruncatedIPTC($val[0], $val[1], 64);
        },DelCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteAlso=>{'IPTC:Keywords'=>'$opts{EditGroup} = 1; $val','XMP-dc:Subject'=>'$val',},},Description=>{Writable=>1,Desire=>{0=>'EXIF:ImageDescription',1=>'IPTC:Caption-Abstract',2=>'XMP-dc:Description',3=>'CurrentIPTCDigest',4=>'IPTCDigest',},RawConv=>q{
            return $val[0] if defined $val[0] and $val[0] !~ /^ *$/;
            return $val[2] if not defined $val[3] or (defined $val[2] and
                             (not defined $val[4] or $val[3] eq $val[4]));
            return Image::ExifTool::MWG::RecoverTruncatedIPTC($val[1], $val[2], 2000);
        },DelCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteAlso=>{'EXIF:ImageDescription'=>'$val','IPTC:Caption-Abstract'=>'$opts{EditGroup} = 1; $val','XMP-dc:Description'=>'$val',},},DateTimeOriginal=>{Description=>'Date/Time Original',Groups=>{2=>'Time' },Notes=>'"specifies when a photo was taken" - MWG',Writable=>1,Shift=>0,Desire=>{0=>'Composite:SubSecDateTimeOriginal',1=>'EXIF:DateTimeOriginal',2=>'IPTC:DateCreated',3=>'IPTC:TimeCreated',4=>'XMP-photoshop:DateCreated',5=>'CurrentIPTCDigest',6=>'IPTCDigest',},RawConv=>q{
            (defined $val[0] or defined $val[1] or $val[2] or
            (defined $val[4] and (not defined $val[5] or not defined $val[6]
            or $val[5] eq $val[6]))) ? $val : undef
        },ValueConv=>q{
            return $val[0] if defined $val[0] and $val[0] !~ /^[: ]*$/;
            return $val[1] if defined $val[1] and $val[1] !~ /^[: ]*$/;
            return $val[4] if not defined $val[5] or (defined $val[4] and
                             (not defined $val[6] or $val[5] eq $val[6]));
            return $val[3] ? "$val[2] $val[3]" : $val[2] if $val[2];
            return undef;
        },PrintConv=>'$self->ConvertDateTime($val)',PrintConvInv=>'$self->InverseDateTime($val,undef,1)',DelCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteAlso=>{'Composite:SubSecDateTimeOriginal'=>'delete $opts{Type}; $val','IPTC:DateCreated'=>'$opts{EditGroup} = 1; $val','IPTC:TimeCreated'=>'$opts{EditGroup} = 1; $val','XMP-photoshop:DateCreated'=>'$val',},},CreateDate=>{Groups=>{2=>'Time' },Notes=>'"specifies when an image was digitized" - MWG',Writable=>1,Shift=>0,Desire=>{0=>'Composite:SubSecCreateDate',1=>'EXIF:CreateDate',2=>'IPTC:DigitalCreationDate',3=>'IPTC:DigitalCreationTime',4=>'XMP-xmp:CreateDate',5=>'CurrentIPTCDigest',6=>'IPTCDigest',},RawConv=>q{
            (defined $val[0] or defined $val[1] or $val[2] or
            (defined $val[4] and (not defined $val[5] or not defined $val[6]
            or $val[5] eq $val[6]))) ? $val : undef
        },ValueConv=>q{
            return $val[0] if defined $val[0] and $val[0] !~ /^[: ]*$/;
            return $val[1] if defined $val[1] and $val[1] !~ /^[: ]*$/;
            return $val[4] if not defined $val[5] or (defined $val[4] and
                             (not defined $val[6] or $val[5] eq $val[6]));
            return $val[3] ? "$val[2] $val[3]" : $val[2] if $val[2];
            return undef;
        },PrintConv=>'$self->ConvertDateTime($val)',PrintConvInv=>'$self->InverseDateTime($val,undef,1)',DelCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteAlso=>{'Composite:SubSecCreateDate'=>'delete $opts{Type}; $val','IPTC:DigitalCreationDate'=>'$opts{EditGroup} = 1; $val','IPTC:DigitalCreationTime'=>'$opts{EditGroup} = 1; $val','XMP-xmp:CreateDate'=>'$val',},},ModifyDate=>{Groups=>{2=>'Time' },Notes=>'"specifies when a file was modified by the user" - MWG',Writable=>1,Shift=>0,Desire=>{0=>'Composite:SubSecModifyDate',1=>'EXIF:ModifyDate',2=>'XMP-xmp:ModifyDate',3=>'CurrentIPTCDigest',4=>'IPTCDigest',},RawConv=>q{
            return $val[0] if defined $val[0] and $val[0] !~ /^[: ]*$/;
            return $val[1] if defined $val[1] and $val[1] !~ /^[: ]*$/;
            return $val[2] if not defined $val[3] or not defined $val[4] or $val[3] eq $val[4];
            return undef;
        },PrintConv=>'$self->ConvertDateTime($val)',PrintConvInv=>'$self->InverseDateTime($val,undef,1)',DelCheck=>'""',WriteCheck=>'""',WriteAlso=>{'Composite:SubSecModifyDate'=>'delete $opts{Type}; $val','XMP-xmp:ModifyDate'=>'$val',},},Orientation=>{Writable=>1,Require=>'EXIF:Orientation',ValueConv=>'$val',PrintConv=>\%Image::ExifTool::Exif::orientation,DelCheck=>'""',WriteCheck=>'""',WriteAlso=>{'EXIF:Orientation'=>'$val',},},Rating=>{Writable=>1,Require=>'XMP-xmp:Rating',ValueConv=>'$val',DelCheck=>'""',WriteCheck=>'""',WriteAlso=>{'XMP-xmp:Rating'=>'$val',},},Copyright=>{Groups=>{2=>'Author' },Writable=>1,Desire=>{0=>'EXIF:Copyright',1=>'IPTC:CopyrightNotice',2=>'XMP-dc:Rights',3=>'CurrentIPTCDigest',4=>'IPTCDigest',},RawConv=>q{
            return $val[0] if defined $val[0] and $val[0] !~ /^ *$/;
            return $val[2] if not defined $val[3] or (defined $val[2] and
                             (not defined $val[4] or $val[3] eq $val[4]));
            return Image::ExifTool::MWG::RecoverTruncatedIPTC($val[1], $val[2], 128);
        },DelCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteAlso=>{'EXIF:Copyright'=>q{
                # encode if necessary (not automatic because Format is 'undef')
                my $enc = $self->Options('CharsetEXIF');
                if ($enc) {
                    my $v = $val;
                    $self->Encode($v,$enc);
                    return $v;
                }
                return $val;
            },'IPTC:CopyrightNotice'=>'$opts{EditGroup} = 1; $val','XMP-dc:Rights'=>'$val',},},Creator=>{Groups=>{2=>'Author' },Flags=>['Writable','List'],Desire=>{0=>'EXIF:Artist',1=>'IPTC:By-line',2=>'XMP-dc:Creator',3=>'CurrentIPTCDigest',4=>'IPTCDigest',},RawConv=>q{
            return $val[0] if defined $val[0] and $val[0] !~ /^ *$/;
            return $val[2] if not defined $val[3] or (defined $val[2] and
                             (not defined $val[4] or $val[3] eq $val[4]));
            return Image::ExifTool::MWG::RecoverTruncatedIPTC($val[1], $val[2], 32);
        },DelCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteAlso=>{'EXIF:Artist'=>'$val','IPTC:By-line'=>'$opts{EditGroup} = 1; $val','XMP-dc:Creator'=>'$val',},},Country=>{Groups=>{2=>'Location' },Writable=>1,Desire=>{0=>'IPTC:Country-PrimaryLocationName',1=>'XMP-photoshop:Country',2=>'XMP-iptcExt:LocationShownCountryName',3=>'CurrentIPTCDigest',4=>'IPTCDigest',},RawConv=>q{
            my $xmpVal = $val[2] || $val[1];
            return $xmpVal if not defined $val[3] or (defined $xmpVal and
                             (not defined $val[4] or $val[3] eq $val[4]));
            return Image::ExifTool::MWG::RecoverTruncatedIPTC($val[0], $xmpVal, 64);
        },DelCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteAlso=>{'IPTC:Country-PrimaryLocationName'=>'$opts{EditGroup} = 1; $val','XMP-photoshop:Country'=>'$val','XMP-iptcExt:LocationShownCountryName'=>'$val',},},State=>{Groups=>{2=>'Location' },Writable=>1,Desire=>{0=>'IPTC:Province-State',1=>'XMP-photoshop:State',2=>'XMP-iptcExt:LocationShownProvinceState',3=>'CurrentIPTCDigest',4=>'IPTCDigest',},RawConv=>q{
            my $xmpVal = $val[2] || $val[1];
            return $xmpVal if not defined $val[3] or (defined $xmpVal and
                             (not defined $val[4] or $val[3] eq $val[4]));
            return Image::ExifTool::MWG::RecoverTruncatedIPTC($val[0], $xmpVal, 32);
        },DelCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteAlso=>{'IPTC:Province-State'=>'$opts{EditGroup} = 1; $val','XMP-photoshop:State'=>'$val','XMP-iptcExt:LocationShownProvinceState'=>'$val',},},City=>{Groups=>{2=>'Location' },Writable=>1,Desire=>{0=>'IPTC:City',1=>'XMP-photoshop:City',2=>'XMP-iptcExt:LocationShownCity',3=>'CurrentIPTCDigest',4=>'IPTCDigest',},RawConv=>q{
            my $xmpVal = $val[2] || $val[1];
            return $xmpVal if not defined $val[3] or (defined $xmpVal and
                             (not defined $val[4] or $val[3] eq $val[4]));
            return Image::ExifTool::MWG::RecoverTruncatedIPTC($val[0], $xmpVal, 32);
        },DelCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteAlso=>{'IPTC:City'=>'$opts{EditGroup} = 1; $val','XMP-photoshop:City'=>'$val','XMP-iptcExt:LocationShownCity'=>'$val',},},Location=>{Groups=>{2=>'Location' },Writable=>1,Desire=>{0=>'IPTC:Sub-location',1=>'XMP-iptcCore:Location',2=>'XMP-iptcExt:LocationShownSublocation',3=>'CurrentIPTCDigest',4=>'IPTCDigest',},RawConv=>q{
            my $xmpVal = $val[2] || $val[1];
            return $xmpVal if not defined $val[3] or (defined $xmpVal and
                             (not defined $val[4] or $val[3] eq $val[4]));
            return Image::ExifTool::MWG::RecoverTruncatedIPTC($val[0], $xmpVal, 32);
        },DelCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteCheck=>'Image::ExifTool::MWG::ReconcileIPTCDigest($self)',WriteAlso=>{'IPTC:Sub-location'=>'$opts{EditGroup} = 1; $val','XMP-iptcCore:Location'=>'$val','XMP-iptcExt:LocationShownSublocation'=>'$val',},},);my%sExtensions=(STRUCT_NAME=>'MWG Extensions',NAMESPACE=>undef,NOTES=>q{
        This structure may contain any top-level XMP tags, but none have been
        pre-defined in ExifTool.  Since no flattened tags have been pre-defined,
        RegionExtensions is writable only as a structure (eg.
        C<{xmp-dc:creator=me,rating=5}>).  Fields for this structure are identified
        using the standard ExifTool tag name (with optional leading group name,
        and/or trailing language code, and/or trailing C<#> symbol to disable print
        conversion).
    },);my%sRegionStruct=(STRUCT_NAME=>'MWG RegionStruct',NAMESPACE=>'mwg-rs',Area=>{Struct=>\%Image::ExifTool::XMP::sArea },Type=>{PrintConv=>{Face=>'Face',Pet=>'Pet',Focus=>'Focus',BarCode=>'BarCode',},},Name=>{},Description=>{},FocusUsage=>{PrintConv=>{EvaluatedUsed=>'Evaluated, Used',EvaluatedNotUsed=>'Evaluated, Not Used',NotEvaluatedNotUsed=>'Not Evaluated, Not Used',},},BarCodeValue=>{},Extensions=>{Struct=>\%sExtensions },Rotation=>{Writable=>'real',Notes=>'not part of MWG 2.0 spec',},seeAlso=>{Namespace=>'rdfs',Resource=>1 },);my%sKeywordStruct;%sKeywordStruct=(STRUCT_NAME=>'MWG KeywordStruct',NAMESPACE=>'mwg-kw',Keyword=>{},Applied=>{Writable=>'boolean' },Children=>{Struct=>\%sKeywordStruct,List=>'Bag' },);%Image::ExifTool::MWG::Regions=(%Image::ExifTool::XMP::xmpTableDefaults,GROUPS=>{0=>'XMP',1=>'XMP-mwg-rs',2=>'Image' },NAMESPACE=>'mwg-rs',NOTES=>q{
        Image region metadata defined by the MWG 2.0 specification.  These tags
        may be accessed without the need to load the MWG Composite tags above.  See
        L<https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf>
        for the official specification.
    },Regions=>{Name=>'RegionInfo',FlatName=>'Region',Struct=>{STRUCT_NAME=>'MWG RegionInfo',NAMESPACE=>'mwg-rs',RegionList=>{FlatName=>'Region',Struct=>\%sRegionStruct,List=>'Bag',},AppliedToDimensions=>{Struct=>\%Image::ExifTool::XMP::sDimensions },},},RegionsRegionList=>{Flat=>1,Name=>'RegionList' },);%Image::ExifTool::MWG::Keywords=(%Image::ExifTool::XMP::xmpTableDefaults,GROUPS=>{0=>'XMP',1=>'XMP-mwg-kw',2=>'Image' },NAMESPACE=>'mwg-kw',NOTES=>q{
        Hierarchical keywords metadata defined by the MWG 2.0 specification. 
        ExifTool unrolls keyword structures to an arbitrary depth of 6 to allow
        individual levels to be accessed with different tag names, and to avoid
        infinite recursion.  See
        L<https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf>
        for the official specification.
    },Keywords=>{Name=>'KeywordInfo',Struct=>{STRUCT_NAME=>'MWG KeywordInfo',NAMESPACE=>'mwg-kw',Hierarchy=>{Struct=>\%sKeywordStruct,List=>'Bag' },},},KeywordsHierarchy=>{Name=>'HierarchicalKeywords',Flat=>1 },KeywordsHierarchyKeyword=>{Name=>'HierarchicalKeywords1',Flat=>1 },KeywordsHierarchyApplied=>{Name=>'HierarchicalKeywords1Applied',Flat=>1 },KeywordsHierarchyChildren=>{Name=>'HierarchicalKeywords1Children',Flat=>1 },KeywordsHierarchyChildrenKeyword=>{Name=>'HierarchicalKeywords2',Flat=>1 },KeywordsHierarchyChildrenApplied=>{Name=>'HierarchicalKeywords2Applied',Flat=>1 },KeywordsHierarchyChildrenChildren=>{Name=>'HierarchicalKeywords2Children',Flat=>1 },KeywordsHierarchyChildrenChildrenKeyword=>{Name=>'HierarchicalKeywords3',Flat=>1 },KeywordsHierarchyChildrenChildrenApplied=>{Name=>'HierarchicalKeywords3Applied',Flat=>1 },KeywordsHierarchyChildrenChildrenChildren=>{Name=>'HierarchicalKeywords3Children',Flat=>1 },KeywordsHierarchyChildrenChildrenChildrenKeyword=>{Name=>'HierarchicalKeywords4',Flat=>1 },KeywordsHierarchyChildrenChildrenChildrenApplied=>{Name=>'HierarchicalKeywords4Applied',Flat=>1 },KeywordsHierarchyChildrenChildrenChildrenChildren=>{Name=>'HierarchicalKeywords4Children',Flat=>1 },KeywordsHierarchyChildrenChildrenChildrenChildrenKeyword=>{Name=>'HierarchicalKeywords5',Flat=>1 },KeywordsHierarchyChildrenChildrenChildrenChildrenApplied=>{Name=>'HierarchicalKeywords5Applied',Flat=>1 },KeywordsHierarchyChildrenChildrenChildrenChildrenChildren=>{Name=>'HierarchicalKeywords5Children',Flat=>1,NoSubStruct=>1 },KeywordsHierarchyChildrenChildrenChildrenChildrenChildrenKeyword=>{Name=>'HierarchicalKeywords6',Flat=>1 },KeywordsHierarchyChildrenChildrenChildrenChildrenChildrenApplied=>{Name=>'HierarchicalKeywords6Applied',Flat=>1 },);%Image::ExifTool::MWG::Collections=(%Image::ExifTool::XMP::xmpTableDefaults,GROUPS=>{0=>'XMP',1=>'XMP-mwg-coll',2=>'Image' },NAMESPACE=>'mwg-coll',NOTES=>q{
        Collections metadata defined by the MWG 2.0 specification.  See
        L<https://web.archive.org/web/20180919181934/http://www.metadataworkinggroup.org/pdf/mwg_guidance.pdf>
        for the official specification.
    },Collections=>{FlatName=>'',List=>'Bag',Struct=>{STRUCT_NAME=>'MWG CollectionInfo',NAMESPACE=>'mwg-coll',CollectionName=>{},CollectionURI=>{},},},);sub Load() {return if$mwgLoaded;Image::ExifTool::AddCompositeTags('Image::ExifTool::MWG');Image::ExifTool::AddTagsToLookup(\%Image::ExifTool::MWG::Composite,'Image::ExifTool::Composite');my$artist=$Image::ExifTool::Exif::Main{0x13b};$$artist{List}=1;$$artist{IsOverwriting}=\&OverwriteStringList;$$artist{RawConv}=\&StringToList;$Image::ExifTool::MWG::strict=1 unless defined$Image::ExifTool::MWG::strict;$mwgLoaded=1}sub ListToString($) {my$vals=shift;for (@$vals){if (/^"/ or /; /){s/"/""/g;$_=qq{"$_"}}}return join('; ',@$vals)}sub StringToList($$) {my ($str,$et)=@_;my (@vals,$inQuotes);my@t=split '; ',$str,-1;for (@t){my$wasQuotes=$inQuotes;$inQuotes=1 if not $inQuotes and s/^"//;if ($inQuotes){$inQuotes=0 if s/((^|[^"])("")*)"$/$1/;s/""/"/g}if ($wasQuotes){$vals[-1].= '; ' .$_}else {push@vals,$_}}$et->Warn('Incorrectly quoted MWG string-list value')if$inQuotes;return@vals > 1 ? \@vals : $vals[0]}sub OverwriteStringList($$$$) {local $_;my ($et,$nvHash,$val,$newValuePt)=@_;my (@new,$delIndex);my$writeMode=$et->Options('WriteMode');if ($writeMode ne 'wcg'){if (defined$val){$writeMode =~ /w/i or return 0}else {$writeMode =~ /c/i or return 0}}if ($$nvHash{DelValue}and defined$val){my$old=StringToList($val,$et);my@old=ref$old eq 'ARRAY' ? @$old : $old;if (@{$$nvHash{DelValue}}){my%del;$del{$_}=1 foreach @{$$nvHash{DelValue}};for (@old){$del{$_}or push(@new,$_),next;$delIndex or $delIndex=scalar@new}}else {push@new,@old}}if ($$nvHash{Value}){if (defined$delIndex){splice@new,$delIndex,0,@{$$nvHash{Value}}}else {push@new,@{$$nvHash{Value}}}}if (@new){$$newValuePt=ListToString(\@new)}else {$$newValuePt=undef}return 1}sub ReconcileIPTCDigest($) {my$et=shift;unless ($Image::ExifTool::Photoshop::iptcDigestInfo and $$et{NEW_VALUE}{$Image::ExifTool::Photoshop::iptcDigestInfo}){my@a;@a=$et->SetNewValue('Photoshop:IPTCDigest','old',Protected=>1,DelValue=>1);@a=$et->SetNewValue('Photoshop:IPTCDigest','new',Protected=>1)}return ''}sub RecoverTruncatedIPTC($$$) {my ($iptc,$xmp,$limit)=@_;return$iptc unless defined$xmp;if (ref$iptc){$xmp=[$xmp ]unless ref$xmp;my ($i,@vals);for ($i=0;$i<@$iptc;++$i){push@vals,RecoverTruncatedIPTC($$iptc[$i],$$xmp[$i],$limit)}return \@vals}elsif (defined$iptc and length$iptc==$limit){$xmp=$$xmp[0]if ref$xmp;return$xmp if length$xmp > $limit and $iptc eq substr($xmp,0,$limit)}return$iptc}1;