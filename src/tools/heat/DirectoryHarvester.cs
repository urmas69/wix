// Copyright (c) .NET Foundation and contributors. All rights reserved. Licensed under the Microsoft Reciprocal License. See LICENSE.TXT file in the project root for full license information.

namespace WixToolset.Harvesters
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using WixToolset.Data;
    using WixToolset.Data.WindowsInstaller;
    using WixToolset.Harvesters.Data;
    using WixToolset.Harvesters.Extensibility;
    using Wix = WixToolset.Harvesters.Serialize;

    /// <summary>
    /// Harvest WiX authoring for a directory from the file system.
    /// </summary>
    public sealed class DirectoryHarvester : BaseHarvesterExtension
    {
        private readonly FileHarvester fileHarvester;

        private const string ComponentPrefix = "cmp";
        private const string DirectoryPrefix = "dir";
        private const string FilePrefix = "fil";

        /// <summary>
        /// Instantiate a new DirectoryHarvester.
        /// </summary>
        public DirectoryHarvester()
        {
            this.fileHarvester = new FileHarvester();
            this.SetUniqueIdentifiers = true;
        }

        public HarvesterFilter Filter = new HarvesterFilter();

        /// <summary>
        /// Gets or sets what type of elements are to be generated.
        /// </summary>
        /// <value>The type of elements being generated.</value>
        public GenerateType GenerateType { get; set; }

        /// <summary>
        /// Gets or sets the option to keep empty directories.
        /// </summary>
        /// <value>The option to keep empty directories.</value>
        public bool KeepEmptyDirectories { get; set; }

        /// <summary>
        /// Gets or sets the rooted DirectoryRef Id if the user has supplied it.
        /// </summary>
        /// <value>The DirectoryRef Id to use as the root.</value>
        public string RootedDirectoryRef { get; set; }

        /// <summary>
        /// Gets of sets the option to set unique identifiers.
        /// </summary>
        /// <value>The option to set unique identifiers.</value>
        public bool SetUniqueIdentifiers { get; set; }

        /// <summary>
        /// Gets or sets the option to suppress including the root directory as an element.
        /// </summary>
        /// <value>The option to suppress including the root directory as an element.</value>
        public bool SuppressRootDirectory { get; set; }

        /// <summary>
        /// Harvest a directory.
        /// </summary>
        /// <param name="argument">The path of the directory.</param>
        /// <returns>The harvested directory.</returns>
        public override Wix.Fragment[] Harvest(string argument)
        {
            if (null == argument)
            {
                throw new ArgumentNullException("argument");
            }

            Wix.IParentElement harvestParent = this.HarvestDirectory(argument, true, this.GenerateType);
            Wix.ISchemaElement harvestElement;

            if (this.GenerateType == GenerateType.PayloadGroup)
            {
                Wix.PayloadGroup payloadGroup = (Wix.PayloadGroup)harvestParent;
                payloadGroup.Id = this.RootedDirectoryRef;
                harvestElement = payloadGroup;
            }
            else
            {
                Wix.Directory directory = (Wix.Directory)harvestParent;

                var directoryRef = DirectoryHelper.CreateDirectoryReference(this.RootedDirectoryRef);

                if (this.SuppressRootDirectory)
                {
                    foreach (Wix.ISchemaElement element in directory.Children)
                    {
                        directoryRef.AddChild(element);
                    }
                }
                else
                {
                    directoryRef.AddChild(directory);
                }
                harvestElement = directoryRef;
            }

            Wix.Fragment fragment = new Wix.Fragment();
            fragment.AddChild(harvestElement);

            return new Wix.Fragment[] { fragment };
        }

        /// <summary>
        /// Harvest a directory.
        /// </summary>
        /// <param name="path">The path of the directory.</param>
        /// <param name="harvestChildren">The option to harvest child directories and files.</param>
        /// <returns>The harvested directory.</returns>
        public Wix.Directory HarvestDirectory(string path, bool harvestChildren)
        {
            if (null == path)
            {
                throw new ArgumentNullException("path");
            }

            return (Wix.Directory)this.HarvestDirectory(path, harvestChildren, GenerateType.Components);
        }

        /// <summary>
        /// Harvest a directory.
        /// </summary>
        /// <param name="path">The path of the directory.</param>
        /// <param name="harvestChildren">The option to harvest child directories and files.</param>
        /// <param name="generateType">The type to generate.</param>
        /// <returns>The harvested directory.</returns>
        private Wix.IParentElement HarvestDirectory(string path, bool harvestChildren, GenerateType generateType)
        {
            if (File.Exists(path))
            {
                throw new WixException(ErrorMessages.ExpectedDirectoryGotFile("dir", path));
            }

            if (null == this.RootedDirectoryRef)
            {
                this.RootedDirectoryRef = "TARGETDIR";
            }

            // use absolute paths
            path = Path.GetFullPath(path);

            // Remove any trailing separator to ensure Path.GetFileName() will return the directory name.
            path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            Wix.IParentElement harvestParent;
            if (generateType == GenerateType.PayloadGroup)
            {
                harvestParent = new Wix.PayloadGroup();
            }
            else
            {
                Wix.Directory directory = new Wix.Directory();
                directory.Name = Path.GetFileName(path);
                directory.FileSource = path;

                if (this.SetUniqueIdentifiers)
                {
                    if (this.SuppressRootDirectory)
                    {
                        directory.Id = this.Core.GenerateIdentifier(DirectoryPrefix, this.RootedDirectoryRef);
                    }
                    else
                    {
                        directory.Id = this.Core.GenerateIdentifier(DirectoryPrefix, this.RootedDirectoryRef, directory.Name);
                    }
                }
                harvestParent = directory;
            }

            if (harvestChildren)
            {
                try
                {
                    int fileCount = this.HarvestDirectory(path, "", harvestParent, generateType);

                    if (generateType != GenerateType.PayloadGroup)
                    {
                        // its an error to not harvest anything with the option to keep empty directories off
                        if (0 == fileCount && !this.KeepEmptyDirectories)
                        {
                            throw new WixException(HarvesterErrors.EmptyDirectory(path));
                        }
                    }
                }
                catch (DirectoryNotFoundException)
                {
                    throw new WixException(HarvesterErrors.DirectoryNotFound(path));
                }
            }

            return harvestParent;
        }

        /// <summary>
        /// Harvest a directory.
        /// </summary>
        /// <param name="path">The path of the directory.</param>
        /// <param name="relativePath">The relative path that will be used when harvesting.</param>
        /// <param name="harvestParent">The directory for this path.</param>
        /// <param name="generateType"></param>
        /// <returns>The number of files harvested.</returns>
        private int HarvestDirectory(string path, string relativePath, Wix.IParentElement harvestParent, GenerateType generateType)
        {
            int fileCount = 0;
            Wix.DirectoryBase directory = generateType != GenerateType.PayloadGroup ? (Wix.DirectoryBase)harvestParent : null;

            // harvest the child directories
            foreach (string childDirectoryPath in Directory.GetDirectories(path))
            {
#if true
                if (this.Filter.IsFiltered(childDirectoryPath))
                {
                    Console.WriteLine("filtered dir: {0}", childDirectoryPath.ToLower());
                    continue;
                }
#endif
  
                var childDirectoryName = Path.GetFileName(childDirectoryPath);
                Wix.IParentElement newParent;
                Wix.Directory childDirectory = null;

                if (generateType == GenerateType.PayloadGroup)
                {
                    newParent = harvestParent;
                }
                else
                {
                    childDirectory = new Wix.Directory();
                    newParent = childDirectory;

                    childDirectory.Name = childDirectoryName;
                    childDirectory.FileSource = childDirectoryPath;

                    if (this.SetUniqueIdentifiers)
                    {
                        childDirectory.Id = this.Core.GenerateIdentifier(DirectoryPrefix, directory.Id, childDirectory.Name);
                    }
                }

                int childFileCount = this.HarvestDirectory(childDirectoryPath, String.Concat(relativePath, childDirectoryName, "\\"), newParent, generateType);

                if (generateType != GenerateType.PayloadGroup)
                {
                    // keep the directory if it contained any files (or empty directories are being kept)
                    if (0 < childFileCount || this.KeepEmptyDirectories)
                    {
                        //Console.WriteLine("harvest dir:{0}", childDirectoryPath);
                        directory.AddChild(childDirectory);
                    }
                }

                fileCount += childFileCount;
            }

            // harvest the files
            string[] files = Directory.GetFiles(path);
            int filesLength = 0;
            if (0 < files.Length)
            {
                foreach (string filePath in Directory.GetFiles(path))
                {
                    string fileName = Path.GetFileName(filePath);

                    if (this.Filter.IsFiltered(filePath))
                    {
                        //Console.WriteLine("filtered file: {0}", filePath.ToLower());
                        continue;
                    }
					
					//Console.WriteLine("harvest file: {0}", filePath);

                    string source = String.Concat("SourceDir\\", relativePath, fileName);

                    Wix.ISchemaElement newChild;
                    if (generateType == GenerateType.PayloadGroup)
                    {
                        Wix.Payload payload = new Wix.Payload();
                        newChild = payload;

                        payload.SourceFile = source;

                        if (!String.IsNullOrEmpty(relativePath))
                        {
                            payload.Name = String.Concat(relativePath, fileName);
                        }
                    }
                    else
                    {
                        Wix.Component component = new Wix.Component();
                        newChild = component;

                        Wix.File file = this.fileHarvester.HarvestFile(filePath);
                        file.Source = source;

                        if (this.SetUniqueIdentifiers)
                        {
                            file.Id = this.Core.GenerateIdentifier(FilePrefix, directory.Id, fileName);
                            component.Id = this.Core.GenerateIdentifier(ComponentPrefix, directory.Id, file.Id);
                        }

                        component.AddChild(file);
                    }

                    harvestParent.AddChild(newChild);
                    filesLength++;
                }
            }
            else if (generateType != GenerateType.PayloadGroup && 0 == fileCount && this.KeepEmptyDirectories)
            {
                Wix.Component component = new Wix.Component();
                component.KeyPath = Wix.YesNoType.yes;

                if (this.SetUniqueIdentifiers)
                {
                    component.Id = this.Core.GenerateIdentifier(ComponentPrefix, directory.Id);
                }

                Wix.CreateFolder createFolder = new Wix.CreateFolder();
                component.AddChild(createFolder);

                directory.AddChild(component);
            }

            return fileCount + filesLength; //files.Length;
        }
    }
}
