using auvdisk.DiskImage;
using auvdisk.Extensions;
using auvdisk.Log;
using DiscUtils;
using Terminal.Gui;

namespace auvdisk.Commander
{
    static class Extensions
    {
        public static string ToHumanString(this FileInfo info)
        {
            return new[]
            {
                $"Name: {info.Name}",
                $"Full path: {info.FullPath}",
                $"Size in bytes: {info.Size}",
                $"Size (human readable): {Utils.HumanizeFilesize(info.Size, true)}",
                $"Created on (UTC): {info.CreatedOnUtc:yyyy-MM-dd HH:mm:ss}",
                $"Modified on (UTC): {info.ModifiedOnUtc:yyyy-MM-dd HH:mm:ss}",
                $"Attributes: {info.Attributes}",
            }.Aggregate((x, y) => $"{x}{Environment.NewLine}{y}");
        }
        
        public static List<IListEntry> FsSource(this ListView list)
        {
            return (list.Source.ToList() as List<IListEntry>)!;
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    static class FsCommander
    {
        public static void OpenDiskImage(string path, ILog logger)
        {
            var fsList = DiskImage.Factory.MakeFsListFromVdisk(path, logger);

            if (fsList is null)
            {
                logger.Error("Failed to interpret image format, didn't find any known filesystems");
                return;
            }

            EntryPoint(fsList, logger);
        }

        public static Flow<None> OpenLocalFs(ILog logger)
        {
            var fsCollection = Factory.MakeFsListFromAvailableVolumes(logger);

            if (!fsCollection.IsErr)
            {
                EntryPoint(fsCollection.UnwrapVal(), logger);
            }

            return fsCollection.Map((x) => None.Value);
        }

        public static void EntryPoint(DiskImage.FsCollection fsCollection, ILog logger)
        {
            Application.Init();
            // TODO: try to pick colors which doesn't make anyone's eyes bleed
            Colors.Base.Normal = Application.Driver.MakeAttribute(Color.BrightYellow, Color.Blue);
            Colors.Base.Focus = Application.Driver.MakeAttribute(Color.White, Color.Cyan);
            Colors.Base.HotNormal = Application.Driver.MakeAttribute(Color.BrightCyan, Color.Blue);
            Colors.Base.HotFocus = Application.Driver.MakeAttribute(Color.BrightBlue, Color.Cyan);

            Colors.Dialog.Normal = Application.Driver.MakeAttribute(Color.Green, Color.Black);
            Colors.Dialog.Focus = Application.Driver.MakeAttribute(Color.Green, Color.Black);

            var top = Application.Top;

            var win = new Window("auvdisk browser - F10 to Exit")
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };

            var leftFrame = new FrameView(fsCollection.Caption)
            {
                X = 0,
                Y = 0,
                Width = Dim.Percent(50),
                Height = Dim.Fill(2)
            };

            var leftPath = new Label(Path.DirectorySeparatorChar.ToString())
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill()
            };

            var leftList = new ListView()
            {
                X = 0,
                Y = 1,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                AllowsMarking = false,
                Data = new DiscUtilsFs(fsCollection.FileSystems),
            };

            leftFrame.Add(leftPath, leftList);

            var rightFrame = new FrameView("Real FS")
            {
                X = Pos.Percent(50),
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(2)
            };

            var rightPath = new Label(Path.DirectorySeparatorChar.ToString())
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill()
            };

            var rightList = new ListView()
            {
                X = 0,
                Y = 1,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                AllowsMarking = false,
                Data = RealDiskFactory.MakeFs(),
            };

            rightFrame.Add(rightPath, rightList);

            var statusBar = new StatusBar([
                new StatusItem(Key.F3, "~F3~ View", () => ViewFile(leftList.HasFocus ? leftList : rightList, leftList.HasFocus ? leftPath : rightPath)),
                new StatusItem(Key.F4, "~F4~ File Info", () => ViewFileInfo(leftList.HasFocus ? leftList : rightList, leftList.HasFocus ? leftPath : rightPath)),
                new StatusItem(Key.F5, "~F5~ Copy", () => CopyFile(leftList, rightList, leftPath, rightPath)),
                new StatusItem(Key.F10, "~F10~ Quit", () => Application.RequestStop())
            ]);

            win.Add(leftFrame, rightFrame);
            top.Add(win, statusBar);

            // Load initial directories
            LoadDirectory(leftList, leftPath);
            LoadDirectory(rightList, rightPath);

            // Handle directory navigation
            leftList.OpenSelectedItem += (e) => NavigateDirectory(leftList, leftPath);
            rightList.OpenSelectedItem += (e) => NavigateDirectory(rightList, rightPath);

            Application.Run();
            Application.Shutdown();
            fsCollection.Dispose();
        }

        static bool LoadDirectory(ListView list, Terminal.Gui.Label pathLabel)
        {
            try
            {
                var state = (list.Data as IFilesystem)!;
                var currentPath = state.Cwd.FullPath;
                var items = new List<IListEntry>();

                if (currentPath.Length > 1)
                {
                    items.Add(new DirEntry(state.PathJoin(currentPath, ".."), state));
                }

                var dirs = state.GetDirectories(currentPath)
                    .Select(d => new DirEntry(d, state))
                    .OrderBy(d => d.Name);
                items.AddRange(dirs);

                var files = state.GetFiles(currentPath)
                    .Select(f => new FileEntry(f, state))
                    .OrderBy(f => f.Name);
                items.AddRange(files);

                list.SetSource(items);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to load directory: {ex.Message}", "OK");
                return false;
            }
        }

        static void NavigateDirectory(ListView list, Terminal.Gui.Label pathLabel)
        {
            var selected = list.FsSource()[list.SelectedItem];
            var state = (list.Data as IFilesystem)!;
            string? newSelected = null;

            if (selected.IsDirectory() || selected.IsDisk())
            {
                var currentPath = state.Cwd.FullPath;
                string newPath;

                if (selected.Name == "..")
                {
                    newPath = state.GetDirectoryName(currentPath);
                    newSelected = state.GetFileName(currentPath);
                }
                else
                {
                    newPath = state.PathJoin(currentPath, selected.Name);
                }

                var prev = pathLabel.Text;
                var prevCwd = state.Cwd;
                pathLabel.Text = newPath;
                state.Cwd = new DirEntry(newPath, state);

                if (!LoadDirectory(list, pathLabel))
                {
                    pathLabel.Text = prev;
                    state.Cwd = prevCwd;
                }
                else if (newSelected != null)
                {
                    var idx = list.FsSource().FindIndex(d => d.Name == newSelected);
                    list.SelectedItem = idx >= 0 ? idx : 0;
                }
            }
        }

        static void ViewFile(ListView list, Terminal.Gui.Label pathLabel)
        {
            try
            {
                var state = (list.Data as IFilesystem)!;
                var selected = list.FsSource()[list.SelectedItem];
                if (!selected.IsFile()) return;

                var filePath = state.PathJoin(state.Cwd.FullPath, selected.Name);

                using var fileStream = state.OpenFile(filePath);

                if (fileStream.Length > 1024 * 1024 * 10) // 10MiB
                {
                    throw new NotSupportedException("File size is too large to preview");
                }

                var reader = new StreamReader(fileStream);
                var fileContent = reader.ReadToEnd();

                var viewDialog = new Dialog($"Viewing: {selected}");

                var textView = new TextView()
                {
                    X = 0,
                    Y = 0,
                    Width = Dim.Fill(),
                    Height = Dim.Fill(1),
                    ReadOnly = true,
                    Text = fileContent
                };

                var closeButton = new Button("Close (ESC)")
                {
                    X = Pos.Center(),
                    Y = Pos.Bottom(textView)
                };
                closeButton.Clicked += () => Application.RequestStop();

                viewDialog.Add(textView, closeButton);
                Application.Run(viewDialog);
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to view file: {ex.Message}", "OK");
            }
        }

        static void ViewFileInfo(ListView list, Terminal.Gui.Label pathLabel)
        {
            try
            {
                var state = (list.Data as IFilesystem)!;
                var selected = list.FsSource()[list.SelectedItem];
                if (!selected.IsFile()) return;

                var filePath = state.PathJoin(state.Cwd.FullPath, selected.Name);
                var fileInfo = state.GetFileInfo(filePath);

                var viewDialog = new Dialog($"File Info: {selected}");

                var textView = new TextView()
                {
                    X = 0,
                    Y = 0,
                    Width = Dim.Fill(),
                    Height = Dim.Fill(1),
                    ReadOnly = true,
                    Text = fileInfo.ToHumanString()
                };

                var closeButton = new Button("Close (ESC)")
                {
                    X = Pos.Center(),
                    Y = Pos.Bottom(textView)
                };
                closeButton.Clicked += () => Application.RequestStop();

                viewDialog.Add(textView, closeButton);
                Application.Run(viewDialog);
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to view file info: {ex.Message}", "OK");
            }
        }

        static void CopyFile(ListView srcList, ListView dstList, Terminal.Gui.Label srcPath, Terminal.Gui.Label dstPath)
        {
            try
            {
                var sourceFs = (srcList.Data as DiscUtilsFs)!;
                var destFs = (dstList.Data as IFilesystem)!;

                var selected = srcList.FsSource()[srcList.SelectedItem];

                var srcFile = sourceFs.PathJoin(srcPath.Text.ToString()!, selected.Name);
                var dstFile = destFs.PathJoin(dstPath.Text.ToString()!, selected.Name);


                if (!srcList.HasFocus)
                {
                    throw new InvalidOperationException("Only copy image->real disk is supported");
                }
                else if (!selected.IsFile())
                {
                    throw new InvalidOperationException("Only copy of the single file supported");
                }
                else if (File.Exists(dstFile))
                {
                    throw new InvalidOperationException("File already exists");
                }
                else if (destFs.Cwd.FullPath == "\\")
                {
                    throw new InvalidOperationException("Please navigate to disk first");
                }

                var result = MessageBox.Query("Copy", $"Copy {selected}?", "Yes", "No");

                if (result == 0)
                {
                    // TODO: progress bar
                    using var dstStream = File.OpenWrite(dstFile);
                    using var srcStream = sourceFs.OpenFile(srcFile);

                    srcStream.CopyTo(dstStream);
                    dstStream.Flush();

                    LoadDirectory(dstList, dstPath);
                    MessageBox.Query("Success", "File copied successfully", "OK");
                }

            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Copy failed: {ex.Message}", "OK");
            }
        }
    }
}