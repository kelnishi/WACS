#!/usr/bin/perl
use strict;
use warnings;
use Fcntl qw(SEEK_CUR SEEK_SET SEEK_END);

# Usage message if no file is given
die "Usage: $0 <file.mov>\n" unless @ARGV == 1;
my $filename = shift;

open my $fh, '<:raw', $filename
  or die "Cannot open '$filename': $!\n";

# Return current position of file handle (for debugging)
sub tell_pos {
    return sprintf("0x%08X", tell($fh));
}

# Read a 32-bit big-endian unsigned integer
sub read_uint32 {
    my ($fh) = @_;
    my $buf;
    my $read = read($fh, $buf, 4);
    return unless $read == 4;
    return unpack("N", $buf);
}

# Read an atom header: 4 bytes size and 4 bytes type.
sub read_atom_header {
    my ($fh) = @_;
    my $size = read_uint32($fh);
    return unless defined $size;
    my $type;
    my $read = read($fh, $type, 4);
    return unless $read == 4;
    return ($size, $type);
}

# Process atoms in the file between the current position and $end.
sub process_atoms {
    my ($fh, $end) = @_;
    while (tell($fh) < $end) {
        my $start = tell($fh);
        my ($atom_size, $atom_type) = read_atom_header($fh);
        unless (defined $atom_size and defined $atom_type) {
            warn "Unable to read atom header at position ", tell_pos(), "\n";
            last;
        }
        print "Found atom '$atom_type' at ", tell_pos(), " with size $atom_size\n";

        # Calculate where this atom ends.
        my $atom_end = $start + $atom_size;

        if ($atom_type eq "Exif" or $atom_type eq "Exif ") {
            # We found an EXIF atom.
            my $exif_data_size = $atom_size - 8; # subtract header size
            my $exif_data;
            my $n = read($fh, $exif_data, $exif_data_size);
            if ($n == $exif_data_size) {
                print "\n=== Raw EXIF Data (", $exif_data_size, " bytes) ===\n";
                # Dump in hex (simple dump)
                for my $i (0 .. length($exif_data)-1) {
                    printf "%02X ", ord(substr($exif_data, $i, 1));
                    print "\n" if ($i+1) % 16 == 0;
                }
                print "\n=== End of EXIF Data ===\n";
            }
            else {
                warn "Failed to read the full EXIF data.\n";
            }
        }
        elsif ($atom_type eq "moov" or $atom_type eq "udta" or $atom_type eq "meta") {
            # These atoms often contain nested atoms.
            # (Note: Some 'meta' atoms start with 4 extra bytes of version/flags.)
            if ($atom_type eq "meta") {
                # Skip version/flags (4 bytes) if present.
                read($fh, my $skip, 4);
            }
            # Recursively process nested atoms.
            process_atoms($fh, $atom_end);
        }
        else {
            # Skip to the end of this atom.
            seek($fh, $atom_end, SEEK_SET)
              or die "Failed to seek in file: $!\n";
        }
    }
}

# Determine file size
seek($fh, 0, SEEK_END) or die "Failed to seek to end of file: $!\n";
my $file_size = tell($fh);
seek($fh, 0, SEEK_SET) or die "Failed to seek to beginning of file: $!\n";

print "Processing '$filename' (", $file_size, " bytes)...\n\n";

process_atoms($fh, $file_size);

close $fh;