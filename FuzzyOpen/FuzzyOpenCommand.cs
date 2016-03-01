//------------------------------------------------------------------------------
// <copyright file="Command1.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.ComponentModel.Design;
using System.Globalization;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE;

namespace OpenDialogTest
{
    class cProjectItemWithMaskComparer : System.Collections.Generic.IEqualityComparer<cProjectItemWithMask>
    {
        public bool Equals(cProjectItemWithMask x, cProjectItemWithMask y)
        {
            return x.mItem == y.mItem;
        }

        public int GetHashCode(cProjectItemWithMask obj)
        {
            return obj.mItem.GetHashCode();
        }
    }
    internal class cProjectItemWithMask
    {
        public UInt64 mMask;
        public ProjectItem mItem;
        internal cProjectItemWithMask(ProjectItem item)
        {
            mMask = sGetMask(item.Name);
            mItem = item;
        }
        internal static UInt64 sGetMask(string name)
        {
            UInt64 mask = 0;
            foreach (char c in name)
            {
                int code = (int)System.Char.ToLower(c) - (int)'a';
                if (code < 64 && code >= 0)
                    mask |= (1u << code);
            }
            return mask;
        }
    }

	/// <summary>
	/// Command handler
	/// </summary>
	internal sealed class FuzzyOpenCommand : Microsoft.VisualStudio.Shell.Interop.IVsSolutionEvents
	{
        private System.Collections.Generic.HashSet<cProjectItemWithMask> mAllItems = new System.Collections.Generic.HashSet<cProjectItemWithMask>(new cProjectItemWithMaskComparer());
		/// <summary>
		/// Command ID.
		/// </summary>
		public const int CommandId = 0x0100;

		/// <summary>
		/// Command menu group (command set GUID).
		/// </summary>
		public static readonly Guid CommandSet = new Guid("1901d828-1fd8-4ebb-91c7-eab752bbc66f");

		/// <summary>
		/// VS Package that provides this command, not null.
		/// </summary>
		private readonly Package package;
        private uint mAdviseId;
        private DTE mDTE;

		/// <summary>
		/// Initializes a new instance of the <see cref="FuzzyOpenCommand"/> class.
		/// Adds our command handlers for menu (commands must exist in the command table file)
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		private FuzzyOpenCommand(Package package)
		{
			if (package == null)
			{
				throw new ArgumentNullException("package");
			}

			this.package = package;
            mAllItems = mLoadAllFiles();
            var svs = FuzzyOpenPackage.GetGlobalService(typeof(SVsSolution)) as IVsSolution;
            if (svs != null)
                svs.AdviseSolutionEvents(this, out mAdviseId);

            mDTE = FuzzyOpenPackage.GetGlobalService(typeof(DTE)) as DTE;
            ((EnvDTE80.Events2)mDTE.Events).ProjectItemsEvents.ItemAdded += ProjectItemsEvents_ItemAdded;
            ((EnvDTE80.Events2)mDTE.Events).ProjectItemsEvents.ItemRemoved += ProjectItemsEvents_ItemRemoved;
            ((EnvDTE80.Events2)mDTE.Events).ProjectItemsEvents.ItemRenamed += ProjectItemsEvents_ItemRenamed;
            //((EnvDTE80.Events2)mDTE.Events).ProjectsEvents.ItemAdded TODO
            //((EnvDTE80.Events2)mDTE.Events).ProjectsEvents.ItemRemoved
            //((EnvDTE80.Events2)mDTE.Events).ProjectsEvents.ItemRenamed

			OleMenuCommandService commandService = this.ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
			if (commandService != null)
			{
				var menuCommandID = new CommandID(CommandSet, CommandId);
				var menuItem = new MenuCommand(this.MenuItemCallback, menuCommandID);
				commandService.AddCommand(menuItem);
			}
		}

        private void ProjectItemsEvents_ItemRenamed(ProjectItem ProjectItem, string OldName)
        {
            mAllItems = mLoadAllFiles(); // TODO : do these in non-ui thread with locking
        }

        private void ProjectItemsEvents_ItemRemoved(ProjectItem ProjectItem)
        {
            mAllItems.Remove(new cProjectItemWithMask(ProjectItem));
        }

        private void ProjectItemsEvents_ItemAdded(ProjectItem ProjectItem)
        {
            mAllItems.Add(new cProjectItemWithMask(ProjectItem));
        }

        private void SolutionEvents_Opened()
        {
            throw new NotImplementedException();
        }

        private void SolutionEvents_ProjectAdded(Project Project)
        {
            var tmp = mAllItems;
            mLoadProject(Project, tmp);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static FuzzyOpenCommand Instance
		{
			get;
			private set;
		}

		/// <summary>
		/// Gets the service provider from the owner package.
		/// </summary>
		private IServiceProvider ServiceProvider
		{
			get
			{
				return this.package;
			}
		}

		/// <summary>
		/// Initializes the singleton instance of the command.
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		public static void Initialize(Package package)
		{
			Instance = new FuzzyOpenCommand(package);
		}

		/// <summary>
		/// This function is the callback used to execute the command when the menu item is clicked.
		/// See the constructor to see how the menu item is associated with this function using
		/// OleMenuCommandService service and MenuCommand class.
		/// </summary>
		/// <param name="sender">Event sender.</param>
		/// <param name="e">Event args.</param>
		private void MenuItemCallback(object sender, EventArgs e)
		{
			IVsUIShell uiShell = (IVsUIShell)ServiceProvider.GetService(typeof(SVsUIShell));
			System.Windows.Window w = new TestWindow(uiShell, mAllItems);

			IntPtr hwnd;
			uiShell.GetDialogOwnerHwnd(out hwnd);
			w.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
			uiShell.EnableModeless(0);
			try {
				Microsoft.Internal.VisualStudio.PlatformUI.WindowHelper.ShowModal(w, hwnd);
			}
			catch(System.Exception exc)
			{
				System.Console.WriteLine(exc.ToString());
			}
			finally
			{
				uiShell.EnableModeless(1);
			}
		}

		private System.Collections.Generic.HashSet<cProjectItemWithMask> mLoadAllFiles()
		{
            var ret = new System.Collections.Generic.HashSet<cProjectItemWithMask>(new cProjectItemWithMaskComparer());
            if (mDTE == null)
                return ret;
            else
            {
                var solution = mDTE.Solution;
                if (!solution.IsOpen)
                    return ret;
                else
                {
                    foreach (Project p in solution.Projects)
                        mLoadProject(p, ret);
                }
            }
            return ret;
		}
        private void mLoadProject(Project p, System.Collections.Generic.HashSet<cProjectItemWithMask> outitems)
        {
            if (p.ProjectItems != null)
                foreach (ProjectItem pi in p.ProjectItems)
                    GetFiles(pi, outitems);
        }

        private void GetFiles(ProjectItem item, System.Collections.Generic.HashSet<cProjectItemWithMask> outitems)
		{
			if (System.Guid.Parse(item.Kind) == Microsoft.VisualStudio.VSConstants.ItemTypeGuid.PhysicalFile_guid)
				outitems.Add(new cProjectItemWithMask(item));
			if (item.ProjectItems != null)
				foreach (ProjectItem i in item.ProjectItems)
				{
					if (System.Guid.Parse(i.Kind) == Microsoft.VisualStudio.VSConstants.ItemTypeGuid.PhysicalFile_guid)
						outitems.Add(new cProjectItemWithMask(i));
					GetFiles(i, outitems);
				}
		}

        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
        {
            mAllItems = mLoadAllFiles();
            return 0;
        }

        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
        {
            return 0;
        }

        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
        {
            return 0;
        }

        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
        {
            mAllItems = mLoadAllFiles();
            return 0;
        }

        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel)
        {
            return 0;
        }

        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
        {
            return 0;
        }

        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            mAllItems = mLoadAllFiles();
            return 0;
        }

        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
        {
            return 0;
        }

        public int OnBeforeCloseSolution(object pUnkReserved)
        {
            return 0;
        }

        public int OnAfterCloseSolution(object pUnkReserved)
        {
            mAllItems = mLoadAllFiles();
            return 0;
        }
    }
}
