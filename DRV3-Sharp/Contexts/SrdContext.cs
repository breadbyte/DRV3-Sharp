﻿/*
    DRV3-Sharp, a free and open-source toolkit
    for working with files and assets from Danganronpa V3.

    Copyright (C) 2020-2021  James Pelster
    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.
    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.
    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DRV3_Sharp_Library.Formats.Resource.SRD;

namespace DRV3_Sharp.Contexts
{
    internal class SrdContext : IOperationContext
    {
        private SrdData? loadedData;
        private string? loadedDataPath;
        private bool unsavedChanges = false;

        public List<IOperation> PossibleOperations
        {
            get
            {
                List<IOperation> operationList = new();

                // Add always-available operations
                operationList.Add(new NewSrdOperation());
                operationList.Add(new LoadSrdOperation());
                operationList.Add(new HelpOperation());
                operationList.Add(new BackOperation());

                // If an SRD file is loaded, add file-related operations
                if (loadedData is not null)
                {
                    operationList.Insert(2, new SaveSrdOperation());
                }

                return operationList;
            }
        }

        public SrdContext()
        { }

        public SrdContext(SrdData initialData, string initialDataPath)
        {
            loadedData = initialData;
            loadedDataPath = initialDataPath;
        }

        protected static SrdContext GetVerifiedContext(IOperationContext compare)
        {
            // Ensure that this is not somehow being called from the wrong context
            if (compare.GetType() != typeof(SrdContext))
                throw new InvalidOperationException($"This operation was called from an illegal context {compare.GetType()}, it should only be called from {typeof(SrdContext)}.");

            return (SrdContext)compare;
        }

        public bool ConfirmIfUnsavedChanges()
        {
            if (unsavedChanges)
            {
                ConsoleColor fgColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("You have unsaved changes pending! These changes WILL BE LOST if you continue!");
                Console.ForegroundColor = fgColor;
                Console.Write("Are you sure you want to continue? (y/N) ");

                var key = Console.ReadKey(false).Key;
                if (key == ConsoleKey.Y)
                    return true;
                else
                    return false;
            }
            else
            {
                return true;
            }
        }

        internal class NewSrdOperation : IOperation
        {
            public string Name => "New SRD";

            public string Description => "Creates a new, empty SRD resource container to be populated.";

            public void Perform(IOperationContext rawContext)
            {
                var context = GetVerifiedContext(rawContext);

                if (!context.ConfirmIfUnsavedChanges()) return;

                context.loadedData = new();
                context.loadedDataPath = null;
                context.unsavedChanges = false;
            }
        }

        internal class LoadSrdOperation : IOperation
        {
            public string Name => "Load SRD";

            public string Description => "Load an existing SRD resource container, and any accompanying binary files.";

            public void Perform(IOperationContext rawContext)
            {
                var context = GetVerifiedContext(rawContext);

                if (!context.ConfirmIfUnsavedChanges()) return;

                // Get the file path
                string? path = Utils.GetPathFromUser("Enter the full path of the file to load (or drag and drop it) and press Enter:");
                if (path is null) return;

                // Load the file now that we've verified it exists
                string srdiPath = Path.ChangeExtension(path, "srdi");
                string srdvPath = Path.ChangeExtension(path, "srdv");
                using FileStream srdStream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                FileStream? srdiStream = null;
                FileStream? srdvStream = null;
                if (File.Exists(srdiPath)) srdiStream = new(srdiPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (File.Exists(srdvPath)) srdvStream = new(srdvPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                SrdSerializer.Deserialize(srdStream, srdvStream, srdiStream, out context.loadedData);
                srdiStream?.Close();
                srdvStream?.Close();

                context.loadedDataPath = path;
                context.unsavedChanges = false;
            }
        }

        internal class SaveSrdOperation : IOperation
        {
            public string Name => "Save SRD";

            public string Description => "Save the currently-loaded SRD resource container, and any accompanying binary files.";

            public void Perform(IOperationContext rawContext)
            {
                var context = GetVerifiedContext(rawContext);

                // Save the file now
                if (string.IsNullOrWhiteSpace(context.loadedDataPath))
                {
                    string? path = Utils.GetPathFromUser("Enter the full path where the file should be saved (or drag and drop it) and press Enter:");
                    if (path is null) return;

                    context.loadedDataPath = path;
                }

                string srdiPath = Path.ChangeExtension(context.loadedDataPath, "srdi");
                string srdvPath = Path.ChangeExtension(context.loadedDataPath, "srdv");
                using FileStream srdStream = new(context.loadedDataPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using FileStream srdiStream = new(srdiPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using FileStream srdvStream = new(srdvPath, FileMode.Create, FileAccess.Write, FileShare.None);
                SrdSerializer.Serialize(context.loadedData!, srdStream, srdvStream, srdiStream);    // It shouldn't be possible to invoke this operation while context.loadedData is null
                srdStream.Flush();

                context.unsavedChanges = false;
            }
        }

        internal class ListBlocksOperation : IOperation
        {
            public string Name => "List Blocks";

            public string Description => "Display a descriptive list of all discrete data blocks in this resource archive.";

            public void Perform(IOperationContext rawContext)
            {
                throw new NotImplementedException();
            }
        }

        internal class ExtractTexturesOperation : IOperation
        {
            public string Name => "Extract Textures";

            public string Description => "Extract one or more textures from the resource container.";

            public void Perform(IOperationContext rawContext)
            {
                var context = GetVerifiedContext(rawContext);

                throw new NotImplementedException();
            }
        }

        internal class HelpOperation : IOperation
        {
            public string Name => "Help";

            public string Description => "Displays information about the operations you can currently perform.";

            public void Perform(IOperationContext rawContext)
            {
                var context = GetVerifiedContext(rawContext);

                var operations = context.PossibleOperations;
                List<(string name, string description)> displayList = new();
                foreach (IOperation op in operations)
                {
                    displayList.Add((op.Name, $"\t{op.Description}"));
                }
                Utils.DisplayDescriptiveList(displayList);

                Console.WriteLine("Press any key to continue...");
                _ = Console.ReadKey(true);
            }
        }

        internal class BackOperation : IOperation
        {
            public string Name => "Back";

            public string Description => "Ends the current SRD operations and returns to the previous screen.";

            public void Perform(IOperationContext rawContext)
            {
                var context = GetVerifiedContext(rawContext);

                if (!context.ConfirmIfUnsavedChanges()) return;

                // Pop this context off the program's context stack
                Program.PopContext();
            }
        }
    }
}
