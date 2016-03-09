//------------------------------------------------------------------------------
// <copyright file="ToolWindow1Control.xaml.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace FuzzyOpen
{
	using EnvDTE;
	using Microsoft.VisualStudio.Shell.Interop;
	using System.Windows.Controls;
	using System;

	internal class cSuggestion : System.IComparable<cSuggestion>
	{
		internal cSuggestion(int matchlength, int matchposition, ProjectItem item)
		{
			mMatchLength = matchlength;
			mMatchPosition = matchposition;
			mItem = item;
			mNameLower = mItem.Name.ToLowerInvariant();
		}
		internal int mMatchLength;
		internal int mMatchPosition;
		internal string mNameLower;
		internal ProjectItem mItem;
		public override string ToString()
		{
			return mItem.Name;
		}

		public int CompareTo(cSuggestion other)
		{
			int ret = mMatchLength - other.mMatchLength;
			if (ret == 0) ret = mMatchPosition - other.mMatchPosition;
			return ret;
		}
		public string Name { get { return mItem.Name; } } 
		public string Path { get { return mItem.FileCount > 0 ? mItem.FileNames[0] : ""; } } 
	}

	public partial class TestWindow : System.Windows.Window
	{
		IVsUIShell mShell;
        System.Collections.Generic.HashSet<cProjectItemWithMask> mAllItems;
		internal TestWindow(IVsUIShell shell, System.Collections.Generic.HashSet<cProjectItemWithMask> allitems)
		{
			mAllItems = allitems;
			mShell = shell;
			InitializeComponent();
			fileNamesGrid.MouseDoubleClick += ListBox_MouseDoubleClick;
			inputTextBox.TextChanged += TextBox_TextChanged;
			inputTextBox.KeyDown += TextBox_KeyDown;
			inputTextBox.PreviewKeyDown += TextBox_PreviewKeyDown;
		}

		private void TextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == System.Windows.Input.Key.Down || (e.Key == System.Windows.Input.Key.Tab && e.KeyboardDevice.Modifiers == System.Windows.Input.ModifierKeys.None))
			{
				if (fileNamesGrid.SelectedIndex < ((System.Collections.Generic.List<cSuggestion>)fileNamesGrid.ItemsSource).Count)
					fileNamesGrid.SelectedIndex++;
				e.Handled = true;
			}
			else if (e.Key == System.Windows.Input.Key.Up || (e.Key == System.Windows.Input.Key.Tab && e.KeyboardDevice.Modifiers == System.Windows.Input.ModifierKeys.Shift))
			{
				if (fileNamesGrid.SelectedIndex > 0)
					fileNamesGrid.SelectedIndex--;
				e.Handled = true;
			}
			else if (e.Key == System.Windows.Input.Key.Escape)
				Close();
		}

		private void TextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == System.Windows.Input.Key.Enter)
				mOpenSelectedSuggestion();
		}

		public Window mWindowToOpen = null;

		private void mOpenSelectedSuggestion()
		{
			var suggestions = (System.Collections.Generic.List<cSuggestion>)fileNamesGrid.ItemsSource;
			if (suggestions.Count > 0)
			{
				int index = fileNamesGrid.SelectedIndex;
				if (index == -1) index = 0;
				var item = suggestions[index].mItem;
				mWindowToOpen = item.Open(EnvDTE.Constants.vsViewKindPrimary);
				Close();
			}
		}

		private class cTaskInfo
		{
			public bool mDone = false;
			public string mPattern;
			public System.Threading.CancellationTokenSource mTokenSource = new System.Threading.CancellationTokenSource();
			public System.Collections.Generic.List<cSuggestion> mSuggestions = null;
		}
		private cTaskInfo mTaskInfo=null;

		private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			var newinfo = new cTaskInfo();
			var oldinfo=System.Threading.Interlocked.Exchange(ref mTaskInfo, newinfo);
			bool refresh=true;
			newinfo.mPattern = inputTextBox.Text.ToLowerInvariant();
			if (oldinfo != null)
			{
				oldinfo.mTokenSource.Cancel();
				if (oldinfo.mDone && newinfo.mPattern.StartsWith(oldinfo.mPattern))
				{
					newinfo.mSuggestions = oldinfo.mSuggestions;
					refresh = false;
				}
			}
			if(refresh)
			{
				UInt64 mask = cProjectItemWithMask.sGetMask(inputTextBox.Text);
				if (newinfo.mSuggestions != null) newinfo.mSuggestions.Clear();
				else newinfo.mSuggestions = new System.Collections.Generic.List<cSuggestion>();
				foreach(var pi in mAllItems)
					if((pi.mMask & mask) == mask)
						newinfo.mSuggestions.Add(new cSuggestion(0, 0, pi.mItem));
				if(newinfo.mPattern.Length<=1) // no point in doing more complicated fuzzing
				{
					fileNamesGrid.ItemsSource = newinfo.mSuggestions.GetRange(0, Math.Min(10, newinfo.mSuggestions.Count));
                    fileNamesGrid.SelectedItem = null;
                    fileNamesGrid.SelectedIndex = 0;
					newinfo.mDone = true;
					return;
				}
			}
			var token = newinfo.mTokenSource.Token;
			System.Threading.Tasks.Task.Run(() => mGetSuggestions(newinfo, this, token), newinfo.mTokenSource.Token);
		}

		private void mUpdateSuggestions(string pattern, System.Collections.Generic.List<cSuggestion> newsuggestions)
		{
				Dispatcher.Invoke(() =>
				{
					if (inputTextBox.Text.ToLowerInvariant() == pattern)
					{
						fileNamesGrid.ItemsSource = newsuggestions.GetRange(0, Math.Min(10, newsuggestions.Count));
                        fileNamesGrid.SelectedItem = null;
						fileNamesGrid.SelectedIndex = 0;// newsource.Count == 0 ? -1 : 0;
						//System.Windows.Data.CollectionViewSource.GetDefaultView(fileNamesGrid.ItemsSource).Refresh();
					}
				});
		}

		private static bool mGetMatch(string pattern, cSuggestion suggestion)
		{
			if (pattern == "") return true;
			int matchlength = 1;
			int matchstart = suggestion.mNameLower.IndexOf(pattern[0]);
			if (matchstart == -1 || matchstart + pattern.Length > suggestion.mNameLower.Length) return false;
			for(int i=1;i<pattern.Length;++i)
				for(;;)
				{
					char c = suggestion.mNameLower[matchlength + matchstart];
					if(c == pattern[i])
						break;
					if(i == 1 && c == pattern[0]) // change match start to this one for shortest match
					{ // TODO : has trouble with repeats in pattern (e.g. "aaaaaa")
						matchstart = matchlength + matchstart;
						matchlength = 1;
					}
					else ++matchlength;
					if (matchlength + matchstart >= suggestion.mNameLower.Length)
						return false;
				}
			suggestion.mMatchLength = matchlength;
			suggestion.mMatchPosition = matchstart;
			return true;
		}

		private static void mGetSuggestions(cTaskInfo info, TestWindow w, System.Threading.CancellationToken token)
		{
			var ret = new System.Collections.Generic.List<cSuggestion>();
			for (int i = 0; i < info.mSuggestions.Count; ++i)
			{
				if (token.IsCancellationRequested) return;
				var sug = info.mSuggestions[i];
				if (mGetMatch(info.mPattern, sug))
					ret.Add(sug);
			}
			ret.Sort();
			info.mSuggestions = ret;
			info.mDone = true;
			w.mUpdateSuggestions(info.mPattern, ret);
		}

		private void ListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
		{
			mOpenSelectedSuggestion();
		}
	}
}