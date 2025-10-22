using DiskAccessLibrary.VHD;
using Spectre.Console;

namespace auvdisk.DiskImage.Vhd
{
    class DifferencingVhdHandler : IDisposable
    {
        private const int BytesPerDiskSector = (int)Util.LbaSize;
        const int HeaderSectorCount = 2;

        private readonly VHDFooter _vhdFooter;
        private readonly byte[] _headerBytes; // used for diagnostics only. Remove when upstream DiskAccessLibrary will merge the bugfixes (hopefully)
        private readonly DynamicDiskHeader _dynamicHeader;
        private readonly FileStream _file;
        private readonly uint[] _batEntries; // Block Allocation Table

        public DifferencingVhdHandler(string path)
        {
            _file = new FileStream(path, FileMode.Open, FileAccess.Read);

            _file.Seek(-BytesPerDiskSector, SeekOrigin.End);
            byte[] footerBytes = new byte[BytesPerDiskSector];
            _file.ReadExactly(footerBytes);

            _vhdFooter = new VHDFooter(footerBytes);

            IEnumerable<VirtualHardDiskType> supportedTypes = 
                [VirtualHardDiskType.Differencing, VirtualHardDiskType.Dynamic];
            
            if (_vhdFooter.IsValid && supportedTypes.Contains(_vhdFooter.DiskType))
            {
                _headerBytes = new byte[BytesPerDiskSector * HeaderSectorCount];
                _file.Seek((long)_vhdFooter.DataOffset, SeekOrigin.Begin);
                _file.ReadExactly(_headerBytes);
                _dynamicHeader = new DynamicDiskHeader(_headerBytes);

                uint maxTableEntries = _dynamicHeader.MaxTableEntries;
                long byteIndex = (long)(_dynamicHeader.TableOffset);
                int byteCount = (int)maxTableEntries * sizeof(UInt32);

                byte[] batBytes = new byte[byteCount];
                _file.Seek(byteIndex, SeekOrigin.Begin);
                _file.ReadExactly(batBytes);

                _batEntries = new uint[maxTableEntries];
                for (int i = 0; i < maxTableEntries; i++)
                {
                    _batEntries[i] = Bytes.Util.FromBigEndianUInt32(batBytes, i * 4);
                }
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public ulong MergeChangedSectorsIntoFixedParent(FileStream target)
        {
            ulong sectorsPerBlock = _dynamicHeader.BlockSize / BytesPerDiskSector;
            int blockBitmapSectorCount = (int)Math.Ceiling((double)sectorsPerBlock / (BytesPerDiskSector * 8)); // TODO: verify
            byte[] bitmap = new byte[blockBitmapSectorCount * BytesPerDiskSector];
            byte[] sector = new byte[BytesPerDiskSector];

            ulong foundSectors = 0;
            
            for (ulong blockIdx = 0; blockIdx < (ulong)_batEntries.Length; ++blockIdx)
            {
                var batEntry = _batEntries[blockIdx];

                if (batEntry == UInt32.MaxValue)
                {
                    continue;
                }
                
                _file.Seek((long)batEntry * BytesPerDiskSector, SeekOrigin.Begin);
                _file.ReadExactly(bitmap);

                for (ulong sectorIdx = 0; sectorIdx < sectorsPerBlock; ++sectorIdx)
                {
                    byte mask = (byte)(1 << (7 - (int)sectorIdx % 8));

                    if ((bitmap[sectorIdx / 8] & mask) != 0)
                    {
                        ++foundSectors;
                        
                        var position = (batEntry + (ulong)blockBitmapSectorCount + sectorIdx) * BytesPerDiskSector;
                        _file.Seek((long)position, SeekOrigin.Begin);
                        _file.ReadExactly(sector);
                        ulong absoluteSectorIdx = blockIdx * sectorsPerBlock + sectorIdx;
                        target.Seek((long)absoluteSectorIdx * BytesPerDiskSector, SeekOrigin.Begin);
                        target.Write(sector);
                    }
                }
            }

            return foundSectors;
        }
        
        public byte[]? ReadSector(ulong sectorIndex)
        {
            // Citation from VHD reference manual:
            // BlockNumber = floor(RawSectorNumber / SectorsPerBlock)
            // SectorInBlock = RawSectorNumber % SectorsPerBlock
            // ActualSectorLocation = BAT[BlockNumber] + BlockBitmapSectorCount + SectorInBlock

            ulong sectorsPerBlock = _dynamicHeader.BlockSize / BytesPerDiskSector;
            ulong blockNumber = sectorIndex / sectorsPerBlock;
            ulong sectorInBlock = sectorIndex % sectorsPerBlock;

            var blockStartInSectors = _batEntries[blockNumber];

            if (blockStartInSectors == UInt32.MaxValue)
            {
                return null;
            }

            byte mask = (byte)(1 << (7 - (int)sectorInBlock % 8));

            int blockBitmapSectorCount = (int)Math.Ceiling((double)sectorsPerBlock / (BytesPerDiskSector * 8)); // TODO: verify

            byte[] bitmap = new byte[blockBitmapSectorCount * BytesPerDiskSector];

            _file.Seek((long)blockStartInSectors * BytesPerDiskSector, SeekOrigin.Begin);
            _file.ReadExactly(bitmap);

            if ((bitmap[sectorInBlock / 8] & mask) != 0)
            {
                var position = (blockStartInSectors + (ulong)blockBitmapSectorCount + sectorInBlock) * BytesPerDiskSector;

                var sector = new byte[BytesPerDiskSector];

                _file.Seek((long)position, SeekOrigin.Begin);
                _file.ReadExactly(sector);

                return sector;
            }
            else
            {
                return null;
            }
        }
        
        public void Dispose()
        {
            _file.Dispose();
        }

        public IEnumerable<(ParentLocatorEntry, string?)> ReadParentLocators()
        {
            var entries = _dynamicHeader.GetParentLocatorEntries();

            return entries.Select(pl =>
            {
                var maybeEntry = DynamicDiskHeader.ReadParentLocator(_file, pl);
                return (pl, maybeEntry);
            });
        }

        public void OutputDiagnosticInfo(Log.ILog logger)
        {
            var dataOffsetBytes = BitConverter.GetBytes(_dynamicHeader.DataOffset);
            
            logger.Log(new Rule("[green]VHD Dynamic Header[/]").LeftJustified());
            
            logger.Log($"[yellow]Header cookie[/]: {_dynamicHeader.Cookie}");
            logger.Log($"[yellow]Data offset[/]: 0x{System.Convert.ToHexString(dataOffsetBytes)}");
            logger.Log($"[yellow]BAT offset in bytes[/]: {_dynamicHeader.TableOffset}");
            logger.Log($"[yellow]Header version[/]: {_dynamicHeader.HeaderVersion}");
            logger.Log($"[yellow]Max BAT table entries[/]: {_dynamicHeader.MaxTableEntries}");
            logger.Log($"[yellow]Parent timestamp[/]: {_dynamicHeader.ParentTimeStamp}");
            logger.Log($"[yellow]Parent name[/]: {_dynamicHeader.ParentUnicodeName}");
            logger.Log($"[yellow]Parent id[/]: {_dynamicHeader.ParentUniqueID}");
            logger.Log($"[yellow]Block size[/]: {_dynamicHeader.BlockSize}");

            ulong usedBatCount = 0;
            
            foreach (var bat in _batEntries)
            {
                if (bat != UInt32.MaxValue)
                {
                    ++usedBatCount;
                }
            }
            
            logger.Log($"[yellow]Used BAT entry count[/]: {usedBatCount}");
            
            logger.Log(new Rule("[green]Diagnostics[/]").LeftJustified());

            var checkFooterCookie = _vhdFooter.Cookie == "conectix";
            var checkHeaderCookie = _dynamicHeader.Cookie == "cxsparse";
            var checkDataOffset = ulong.MaxValue == _dynamicHeader.DataOffset;
            var checkBatSize = _dynamicHeader.TableOffset % Util.LbaSize == 0;
            var checkFooterDataOffset = _vhdFooter.DataOffset == Util.LbaSize;
            var checkMaxTableEntries =
                _dynamicHeader.MaxTableEntries == _vhdFooter.CurrentSize / _dynamicHeader.BlockSize;
            var checkHeaderValid = Util.CalculateChecksum(_headerBytes, 0x24) ==
                                   Bytes.Util.FromBigEndianUInt32(_headerBytes, 0x24);

            var checkString = (bool check) => check ? "[green]success[/]" : "[red]fail[/]";
            
            logger.Log($"[yellow]Footer cookie check[/]: {checkString(checkFooterCookie)}");
            logger.Log($"[yellow]Header cookie check[/]: {checkString(checkHeaderCookie)}");
            logger.Log($"[yellow]Data offset check[/]: {checkString(checkDataOffset)}");
            logger.Log($"[yellow]BAT size check[/]: {checkString(checkBatSize)}");
            logger.Log($"[yellow]Footer offset check[/]: {checkString(checkFooterDataOffset)}");
            logger.Log($"[yellow]Max BAT entries check[/]: {checkString(checkMaxTableEntries)}");
            logger.Log($"[yellow]Header checksum check[/]: {checkString(checkHeaderValid)}");

            if (_vhdFooter.DiskType == VirtualHardDiskType.Differencing)
            {
                logger.Log(new Rule("[green]Parent locator entries[/]").LeftJustified());

                foreach (var locator in _dynamicHeader.GetParentLocatorEntries()
                             .Select((locator, index) => (locator, index)))
                {
                    logger.Log(
                        $"[magenta]({locator.index}) [/][yellow]Platform code[/]: {(DynamicDiskHeader.ParentLocatorPlatformCode)locator.locator.PlatformCode}");
                    logger.Log(
                        $"[magenta]({locator.index}) [/][yellow]Value[/]: {DynamicDiskHeader.ReadParentLocator(_file, locator.locator)}");
                    logger.Log($"[magenta]({locator.index}) [/][yellow]Offset[/]: {locator.locator.PlatformDataOffset}");
                    logger.Log($"[magenta]({locator.index}) [/][yellow]Length[/]: {locator.locator.PlatformDataLength}");
                    logger.Log($"[magenta]({locator.index}) [/][yellow]Space[/]: {locator.locator.PlatformDataSpace}");
                }

                logger.Log(new Rule("[green]End of parent locator entries[/]").LeftJustified());
            }
            else
            {
                logger.Log($"[yellow]No parent locator entries check[/]: {checkString(!_dynamicHeader.GetParentLocatorEntries().Any())}");
                logger.Log(new Rule("[green]End of diagnostics[/]").LeftJustified());
            }
        }
    }
}
