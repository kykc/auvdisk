using DiskAccessLibrary;
using DiskAccessLibrary.VHD;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace auvdisk
{
    internal static class DalUtils
    {
        private static ulong RoundUp(ulong numToRound, ulong multiple)
        {
            if (multiple == 0)
                return numToRound;

            ulong remainder = numToRound % multiple;
            if (remainder == 0)
                return numToRound;

            return numToRound + multiple - remainder;
        }

        private static VHDFooter CreateVhdFooter(ulong size)
        {
            VHDFooter vhdFooter = new VHDFooter();
            vhdFooter.OriginalSize = size;
            vhdFooter.CurrentSize = size;
            vhdFooter.SetCurrentTimeStamp();
            vhdFooter.SetDiskGeometry(size / Program.LbaSize);

            return vhdFooter;
        }

        public static VirtualHardDisk CreateFixedVhd(string path, ulong size, Action<String> logger, bool forceZeroFill = false)
        {
            if (size % Program.LbaSize > 0)
            {
                size = RoundUp(size, Program.LbaSize);
                logger($"WARNING: VHD size must be a multiple of {Program.LbaSize}, rounded up size to {size}");
            }

            // Faster due to disabled Windows FS security (resulting file contains contents from real disk, potential security issue)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && CliTools.IsVhdToolPresent() && !forceZeroFill)
            {
                logger("Found VhdTool, will use it to speed things up. You may receive UAC prompt");
                CliTools.CreateWithVhdTool(path, size);
                
                return new VirtualHardDisk(path);
            }
            else if (CliTools.IsDdPresent() && !forceZeroFill)
            {
                logger("Found dd, will allocate VHD using it to speed things up");
                logger("WARNING: this may result in a sparse file, and Windows doesn't like sparse VHDs");

                CliTools.AllocateWithDd(path, size);

                using (var stream = new FileStream(path, FileMode.Open))
                {
                    stream.Seek(0, SeekOrigin.End);
                    stream.Write(CreateVhdFooter(size).GetBytes());
                }

                return new VirtualHardDisk(path);
            }
            else
            {
                logger("Using DiskAccessLibrary to create VHD");
                logger("Will do full-length zeroing which is slow");
                logger("This is needed to avoid creating a sparse file. Known to happen on Linux when writing to Samba share");

                byte[] nullSector = Enumerable.Repeat((byte)0x0, (int)Program.LbaSize).ToArray();
                ulong sectorCount = size / Program.LbaSize;

                using (var stream = new FileStream(path, FileMode.CreateNew))
                {
                    for (ulong sector = 0; sector < sectorCount; ++sector)
                    {
                        stream.Write(nullSector);
                    }

                    stream.Write(CreateVhdFooter(size).GetBytes());
                }

                return new VirtualHardDisk(path);
            }
        }

        public static ulong CreateBootableFixedVhdLayout(string target, ulong bootSizeInBytes, ulong dataSizeInBytes, Action<string> logger, bool zeroFill = false)
        {
            logger("Creating fixed VHD layout");
            dataSizeInBytes = RoundUp(dataSizeInBytes, Program.LbaSize);
            logger("Rounded up data partition size is " + dataSizeInBytes.ToString());

            const ulong offsetLba = 2048UL; // Start first partition from the sector/LBA 2048, this is what Windows does AFAIK
            const ulong overheadSize = 1024UL * 1024UL; // 1MiB for partition table and stuff

            ulong totalSize = offsetLba * Program.LbaSize + overheadSize + bootSizeInBytes + dataSizeInBytes;
            logger("Total size of the image contents is " + totalSize.ToString());
            var vdisk = CreateFixedVhd(target, totalSize, logger, zeroFill);

            List<GuidPartitionEntry> list = new List<GuidPartitionEntry>();

            GuidPartitionEntry bootPartitionEntry = new GuidPartitionEntry();
            bootPartitionEntry.PartitionGuid = Guid.NewGuid();
            bootPartitionEntry.PartitionTypeGuid = GPTPartition.EFISystemPartitionTypeGuid;
            bootPartitionEntry.FirstLBA = offsetLba;
            bootPartitionEntry.LastLBA = offsetLba + bootSizeInBytes / Program.LbaSize - 1;
            bootPartitionEntry.PartitionName = "Boot";
            list.Add(bootPartitionEntry);

            GuidPartitionEntry dataPartitionEntry = new GuidPartitionEntry();
            dataPartitionEntry.PartitionGuid = Guid.NewGuid();
            dataPartitionEntry.PartitionTypeGuid = GPTPartition.BasicDataPartititionTypeGuid;
            dataPartitionEntry.FirstLBA = bootPartitionEntry.LastLBA + 1;
            // This one is a bit magical to me, snatched from DiscAccessLibrary internals. Probably accounts for GPT partition table footer
            dataPartitionEntry.LastLBA = (ulong)(vdisk.TotalSectors - 1 - 16384 / vdisk.BytesPerSector) - 1;
            list.Add(dataPartitionEntry);

            logger("Initializing VHD with GPT partition table");
            logger("Boot partition space in LBA is from " + bootPartitionEntry.FirstLBA.ToString() + " to " + bootPartitionEntry.LastLBA.ToString());
            logger("Data partition space in LBA is from " + dataPartitionEntry.FirstLBA.ToString() + " to " + dataPartitionEntry.LastLBA.ToString());
            DiskAccessLibrary.GuidPartitionTable.InitializeDisk(vdisk, (long)offsetLba, list);

            return dataSizeInBytes;
        }
    }
}
