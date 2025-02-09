// Copyright (c) .NET Foundation and contributors. All rights reserved. Licensed under the Microsoft Reciprocal License. See LICENSE.TXT file in the project root for full license information.

namespace WixToolset.Harvesters
{
    using System;
    using System.Collections;
    using System.IO;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using WixToolset.Data;
    using WixToolset.Harvesters.Data;
    using WixToolset.Harvesters.Extensibility;
    using Wix = WixToolset.Harvesters.Serialize;

    /// <summary>
    /// The WiX Toolset harvester mutator.
    /// </summary>
    public sealed class UtilHarvesterMutator : BaseMutatorExtension
    {
        // Flags for SetErrorMode() native method.
        private const UInt32 SEM_FAILCRITICALERRORS = 0x0001;
        private const UInt32 SEM_NOGPFAULTERRORBOX = 0x0002;
        private const UInt32 SEM_NOALIGNMENTFAULTEXCEPT = 0x0004;
        private const UInt32 SEM_NOOPENFILEERRORBOX = 0x8000;

        // Remember whether we were able to call OaEnablePerUserTLibRegistration
        private bool calledPerUserTLibReg;

        /// <summary>
        /// allow process to handle serious system errors.
        /// </summary>
        [DllImport("Kernel32.dll")]
        private static extern void SetErrorMode(UInt32 uiMode);

        /// <summary>
        /// enable the RegisterTypeLib API to use the appropriate override mapping for non-admin users on Vista
        /// </summary>
        [DllImport("Oleaut32.dll")]
        private static extern void OaEnablePerUserTLibRegistration();

        public UtilHarvesterMutator()
        {
            this.calledPerUserTLibReg = false;

            SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX);

            try
            {
                OaEnablePerUserTLibRegistration();
                this.calledPerUserTLibReg = true;
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        /// <summary>
        /// Gets the sequence of this mutator extension.
        /// </summary>
        /// <value>The sequence of this mutator extension.</value>
        public override int Sequence
        {
            get { return 100; }
        }

        public Platform? Platform { get; internal set; }

        /// <summary>
        /// Mutate a WiX document.
        /// </summary>
        /// <param name="wix">The Wix document element.</param>
        public override void Mutate(Wix.Wix wix)
        {
            this.MutateElement(null, wix);
        }

        /// <summary>
        /// Mutate an element.
        /// </summary>
        /// <param name="parentElement">The parent of the element to mutate.</param>
        /// <param name="element">The element to mutate.</param>
        private void MutateElement(Wix.IParentElement parentElement, Wix.ISchemaElement element)
        {
            if (element is Wix.File)
            {
                this.MutateFile(parentElement, (Wix.File)element);
            }

            // mutate the child elements
            if (element is Wix.IParentElement)
            {
                ArrayList childElements = new ArrayList();

                // copy the child elements to a temporary array (to allow them to be deleted/moved)
                foreach (Wix.ISchemaElement childElement in ((Wix.IParentElement)element).Children)
                {
                    childElements.Add(childElement);
                }

                foreach (Wix.ISchemaElement childElement in childElements)
                {
                    this.MutateElement((Wix.IParentElement)element, childElement);
                }
            }
        }

        /// <summary>
        /// Mutate a file.
        /// </summary>
        /// <param name="parentElement">The parent of the element to mutate.</param>
        /// <param name="file">The file to mutate.</param>
        private void MutateFile(Wix.IParentElement parentElement, Wix.File file)
        {
            if (null != file.Source)
            {
                string fileExtension = Path.GetExtension(file.Source);
                string fileSource = this.Core.ResolveFilePath(file.Source);

                if ((String.Equals(".ax", fileExtension, StringComparison.OrdinalIgnoreCase) || // DirectShow filter
                    String.Equals(".dll", fileExtension, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(".exe", fileExtension, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(".ocx", fileExtension, StringComparison.OrdinalIgnoreCase)) // ActiveX
                    && HarvesterFilter.Instance<UtilHarvesterMutator>().IsIncl(fileSource)
                    ) // ActiveX
                {

                    Console.WriteLine("libs: {0}", fileSource);
                    //System.Diagnostics.Debugger.Launch();
                    // try the assembly harvester
                    try
                    {
                        AssemblyHarvester assemblyHarvester = new AssemblyHarvester();

                        this.Core.Messaging.Write(HarvesterVerboses.HarvestingAssembly(fileSource));
                        Wix.RegistryValue[] registryValues = assemblyHarvester.HarvestRegistryValues(fileSource);

                        foreach (Wix.RegistryValue registryValue in registryValues)
                        {
                            parentElement.AddChild(registryValue);
                        }

                        // also try self-reg since we could have a mixed-mode assembly
                        this.HarvestSelfReg(parentElement, fileSource);
                    }
                    catch (BadImageFormatException) // not an assembly, try raw DLL.
                    {
                        this.HarvestSelfReg(parentElement, fileSource);
                    }
                    catch (Exception ex)
                    {
                        this.Core.Messaging.Write(HarvesterWarnings.AssemblyHarvestFailed(fileSource, ex.Message));
                    }
                }
                else if (String.Equals(".olb", fileExtension, StringComparison.OrdinalIgnoreCase) || // type library
                          String.Equals(".tlb", fileExtension, StringComparison.OrdinalIgnoreCase)
                          //|| this.OlbsFilter.IsIncl(fileSource)
                          ) // type library
                {
                    Console.WriteLine("olbs: {0}", fileSource);
                    // try the type library harvester
                    try
                    {
                        TypeLibraryHarvester typeLibHarvester = new TypeLibraryHarvester();

                        this.Core.Messaging.Write(HarvesterVerboses.HarvestingTypeLib(fileSource));
                        Wix.RegistryValue[] registryValues = typeLibHarvester.HarvestRegistryValues(fileSource);

                        foreach (Wix.RegistryValue registryValue in registryValues)
                        {
                            parentElement.AddChild(registryValue);
                        }
                    }
                    catch (COMException ce)
                    {
                        //  0x8002801C (TYPE_E_REGISTRYACCESS)
                        // If we don't have permission to harvest typelibs, it's likely because we're on
                        // Vista or higher and aren't an Admin, or don't have the appropriate QFE installed.
                        if (!this.calledPerUserTLibReg && (0x8002801c == unchecked((uint)ce.ErrorCode)))
                        {
                            this.Core.Messaging.Write(WarningMessages.InsufficientPermissionHarvestTypeLib());
                        }
                        else if (0x80029C4A == unchecked((uint)ce.ErrorCode)) // generic can't load type library
                        {
                            this.Core.Messaging.Write(HarvesterWarnings.TypeLibLoadFailed(fileSource, ce.Message));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Calls self-reg harvester.
        /// </summary>
        /// <param name="parentElement">The parent element.</param>
        /// <param name="fileSource">The file source.</param>
        private void HarvestSelfReg(Wix.IParentElement parentElement, string fileSource)
        {
           // try the self-reg harvester
           try
           {
              DllHarvester dllHarvester = new DllHarvester();

              this.Core.Messaging.Write(HarvesterVerboses.HarvestingSelfReg(fileSource));
              Wix.RegistryValue[] registryValues = dllHarvester.HarvestRegistryValues(fileSource);

              //System.Diagnostics.Debugger.Launch();
              foreach (Wix.RegistryValue registryValue in registryValues)
              {
                 parentElement.AddChild(registryValue);
              }

              if (this.Platform!=null && registryValues.Length > 0)
              {
                    if (parentElement is Wix.Component component)
                    {
                        // Wenn der DLL-Harvester Werte extrahiert hat, ist dies eine 32-Bit-Komponente
                        if(this.Platform==WixToolset.Data.Platform.X86)
                        {
                            component.Win64 =  Wix.YesNoType.no;
                        }
                        else
                        {
                            component.Win64 = Wix.YesNoType.yes;
                        }
                        //not working component.DisableRegistryReflection = Wix.YesNoType.yes;
                    }
                }
           }
           catch (TargetInvocationException tie)
           {
              if (tie.InnerException is EntryPointNotFoundException)
              {
                 // No DllRegisterServer(), which is fine by me.
              }
              else
              {
                 this.Core.Messaging.Write(HarvesterWarnings.SelfRegHarvestFailed(fileSource, tie.Message));
              }
           }
           catch (Exception ex)
           {
              this.Core.Messaging.Write(HarvesterWarnings.SelfRegHarvestFailed(fileSource, ex.Message));
           }
        }
    }
}
