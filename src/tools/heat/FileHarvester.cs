// Copyright (c) .NET Foundation and contributors. All rights reserved. Licensed under the Microsoft Reciprocal License. See LICENSE.TXT file in the project root for full license information.

namespace WixToolset.Harvesters
{
    using System;
    using System.IO;
    using WixToolset.Data;
    using WixToolset.Harvesters.Data;
    using WixToolset.Harvesters.Extensibility;
    using Wix = WixToolset.Harvesters.Serialize;

    /// <summary>
    /// Harvest WiX authoring for a file from the file system.
    /// </summary>
    public sealed class FileHarvester : BaseHarvesterExtension
    {
        private string rootedDirectoryRef;
        private bool setUniqueIdentifiers;
        private bool suppressRootDirectory;

        private static readonly string ComponentPrefix = "cmp";
        private static readonly string DirectoryPrefix = "dir";
        private static readonly string FilePrefix = "fil";

        /// <summary>
        /// Instantiate a new FileHarvester.
        /// </summary>
        public FileHarvester()
        {
            this.setUniqueIdentifiers = true;
            this.suppressRootDirectory = false;
        }

        /// <summary>
        /// Gets or sets the rooted DirectoryRef Id if the user has supplied it.
        /// </summary>
        /// <value>The DirectoryRef Id to use as the root.</value>
        public string RootedDirectoryRef
        {
            get { return this.rootedDirectoryRef; }
            set { this.rootedDirectoryRef = value; }
        }

        /// <summary>
        /// Gets of sets the option to set unique identifiers.
        /// </summary>
        /// <value>The option to set unique identifiers.</value>
        public bool SetUniqueIdentifiers
        {
            get { return this.setUniqueIdentifiers; }
            set { this.setUniqueIdentifiers = value; }
        }

        /// <summary>
        /// Gets or sets the option to suppress including the root directory as an element.
        /// </summary>
        /// <value>The option to suppress including the root directory as an element.</value>
        public bool SuppressRootDirectory
        {
            get { return this.suppressRootDirectory; }
            set { this.suppressRootDirectory = value; }
        }

        /// <summary>
        /// Harvest a file.
        /// </summary>
        /// <param name="argument">The path of the file.</param>
        /// <returns>A harvested file.</returns>
        public override Wix.Fragment[] Harvest(string argument)
        {
            if (null == argument)
            {
                throw new ArgumentNullException("argument");
            }

            if (null == this.rootedDirectoryRef)
            {
                this.rootedDirectoryRef = "TARGETDIR";
            }

            string fullPath = Path.GetFullPath(argument);

            var directoryRef = DirectoryHelper.CreateDirectoryReference(this.rootedDirectoryRef);

            Wix.File file = this.HarvestFile(fullPath);

            if (!this.suppressRootDirectory)
            {
                file.Source = String.Concat("SourceDir\\", Path.GetFileName(Path.GetDirectoryName(fullPath)), "\\", Path.GetFileName(fullPath));
            }

            Wix.Component component = new Wix.Component();
            component.AddChild(file);
            //component.Win64 = Wix.YesNoType.no;

            Wix.Directory directory = new Wix.Directory();

            if (this.suppressRootDirectory)
            {
                directoryRef.AddChild(component);
            }
            else
            {
                string directoryPath = Path.GetDirectoryName(Path.GetFullPath(argument));
                directory.Name = Path.GetFileName(directoryPath);

                if (this.setUniqueIdentifiers)
                {
                    directory.Id = this.Core.GenerateIdentifier(DirectoryPrefix, directoryRef.Id, directory.Name);
                }
                directory.AddChild(component);
                directoryRef.AddChild(directory);
            }

            if (this.setUniqueIdentifiers)
            {
                file.Id = this.Core.GenerateIdentifier(FilePrefix, (this.suppressRootDirectory) ? directoryRef.Id : directory.Id, Path.GetFileName(file.Source));
                component.Id = this.Core.GenerateIdentifier(ComponentPrefix, (this.suppressRootDirectory) ? directoryRef.Id : directory.Id, file.Id);
            }

            Wix.Fragment fragment = new Wix.Fragment();
            fragment.AddChild(directoryRef);

            return new Wix.Fragment[] { fragment };
        }

        /// <summary>
        /// Harvest a file.
        /// </summary>
        /// <param name="path">The path of the file.</param>
        /// <returns>A harvested file.</returns>
        public Wix.File HarvestFile(string path)
        {
            if (null == path)
            {
                throw new ArgumentNullException("path");
            }

            if (!File.Exists(path))
            {
                throw new WixException(HarvesterErrors.FileNotFound(path));
            }

            Wix.File file = new Wix.File();

            // use absolute paths
            path = Path.GetFullPath(path);

            file.KeyPath = Wix.YesNoType.yes;

            file.Source = String.Concat("SourceDir\\", Path.GetFileName(path));

            return file;
        }
    }
}
