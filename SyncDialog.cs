/*
  KeePassGitHubSecrets - push entry custom fields to GitHub Actions secrets
  Copyright (C) 2026 shijianjs

  This program is free software; you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation; either version 2 of the License, or
  (at your option) any later version. See LICENSE for the full text.
*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace KeePassGitHubSecrets
{
	/// <summary>
	/// Confirmation and progress window: pick repositories and secrets, then run
	/// one gh secret set per pair on a worker thread.
	/// </summary>
	public sealed class SyncDialog : Form
	{
		private readonly SecretEntryModel m_model;

		private Label m_lblSummary = null;
		private Label m_lblWarnings = null;
		private Label m_lblStatus = null;
		private CheckedListBox m_clbRepositories = null;
		private CheckedListBox m_clbSecrets = null;
		private ListView m_lvResults = null;
		private ProgressBar m_pbProgress = null;
		private Button m_btnSync = null;
		private Button m_btnClose = null;

		// Touched from the worker thread: keep it volatile.
		private volatile bool m_bBusy = false;

		// KeePass runs DPI-aware, so the dialog has to scale itself: WinForms'
		// font auto-scaling does not kick in here, and hard-coded 96-dpi
		// coordinates end up under a 200% font. Every size below is multiplied
		// by this factor.
		private float m_fScale = 1f;

		private int S(int value) { return (int)Math.Round(value * m_fScale); }
		private Size Sz(int width, int height) { return new Size(S(width), S(height)); }

		public SyncDialog(SecretEntryModel model)
		{
			if(model == null) throw new ArgumentNullException("model");

			m_model = model;
			BuildUi();
			FillLists();
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if(m_bBusy)
			{
				e.Cancel = true;
				m_lblStatus.Text = "Wait for the running sync to finish.";
			}
			base.OnFormClosing(e);
		}

		private void BuildUi()
		{
			SuspendLayout();

			Font = SystemFonts.MessageBoxFont;
			using(Graphics graphics = CreateGraphics())
			{
				m_fScale = graphics.DpiX / 96f;
			}
			if(m_fScale < 1f) m_fScale = 1f;

			// Manual scaling: leave WinForms auto-scaling out of it.
			AutoScaleMode = AutoScaleMode.None;

			Text = "Sync to GitHub Secrets";
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			ClientSize = Sz(780, 560);

			int lineHeight = Font.Height;
			int pad = S(12);
			int gap = S(6);
			int captionHeight = lineHeight + S(6); // GroupBox title area

			m_lblSummary = new Label();
			m_lblSummary.Dock = DockStyle.Fill;
			m_lblSummary.AutoEllipsis = true;
			m_lblSummary.TextAlign = ContentAlignment.MiddleLeft;
			m_lblSummary.Margin = new Padding(0, 0, 0, gap);

			m_clbRepositories = new CheckedListBox();
			m_clbRepositories.Dock = DockStyle.Fill;
			m_clbRepositories.CheckOnClick = true;
			m_clbRepositories.IntegralHeight = false;

			GroupBox grpRepositories = new GroupBox();
			grpRepositories.Text = "Repositories (" + SecretEntryModel.RepositoryField + ")";
			grpRepositories.Dock = DockStyle.Fill;
			grpRepositories.Padding = new Padding(S(8), captionHeight, S(8), S(8));
			grpRepositories.Margin = new Padding(0, 0, gap, 0);
			grpRepositories.Controls.Add(m_clbRepositories);

			m_clbSecrets = new CheckedListBox();
			m_clbSecrets.Dock = DockStyle.Fill;
			m_clbSecrets.CheckOnClick = true;
			m_clbSecrets.IntegralHeight = false;

			GroupBox grpSecrets = new GroupBox();
			grpSecrets.Text = "Secrets (field names, values stay hidden)";
			grpSecrets.Dock = DockStyle.Fill;
			grpSecrets.Padding = new Padding(S(8), captionHeight, S(8), S(8));
			grpSecrets.Margin = new Padding(0);
			grpSecrets.Controls.Add(m_clbSecrets);

			TableLayoutPanel lists = new TableLayoutPanel();
			lists.Dock = DockStyle.Fill;
			lists.ColumnCount = 2;
			lists.RowCount = 1;
			lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
			lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56F));
			lists.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			lists.Margin = new Padding(0, 0, 0, gap);
			lists.Controls.Add(grpRepositories, 0, 0);
			lists.Controls.Add(grpSecrets, 1, 0);

			m_lblWarnings = new Label();
			m_lblWarnings.Dock = DockStyle.Fill;
			m_lblWarnings.ForeColor = Color.Firebrick;
			m_lblWarnings.AutoEllipsis = true;
			m_lblWarnings.TextAlign = ContentAlignment.TopLeft;
			m_lblWarnings.Margin = new Padding(0, 0, 0, gap);

			m_lvResults = new ListView();
			m_lvResults.Dock = DockStyle.Fill;
			m_lvResults.View = View.Details;
			m_lvResults.FullRowSelect = true;
			m_lvResults.GridLines = true;
			m_lvResults.HideSelection = false;
			m_lvResults.MultiSelect = false;
			m_lvResults.Margin = new Padding(0, 0, 0, gap);
			m_lvResults.Columns.Add("Repository", S(200));
			m_lvResults.Columns.Add("Secret", S(210));
			m_lvResults.Columns.Add("Result", S(70));
			m_lvResults.Columns.Add("Message", S(260));

			m_lblStatus = new Label();
			m_lblStatus.Dock = DockStyle.Fill;
			m_lblStatus.TextAlign = ContentAlignment.MiddleLeft;
			m_lblStatus.Text = "Ready.";
			m_lblStatus.Margin = new Padding(0);

			m_pbProgress = new ProgressBar();
			m_pbProgress.Dock = DockStyle.Fill;
			m_pbProgress.Margin = new Padding(0, S(6), gap, 0);

			m_btnSync = new Button();
			m_btnSync.Text = "Sync";
			m_btnSync.AutoSize = true;
			m_btnSync.MinimumSize = Sz(84, 28);
			m_btnSync.Margin = new Padding(0, 0, S(4), 0);
			m_btnSync.Click += this.OnSyncClick;

			m_btnClose = new Button();
			m_btnClose.Text = "Close";
			m_btnClose.AutoSize = true;
			m_btnClose.MinimumSize = Sz(84, 28);
			m_btnClose.Margin = new Padding(0);
			m_btnClose.DialogResult = DialogResult.Cancel;

			// RightToLeft: the first control added sits at the right edge.
			FlowLayoutPanel buttons = new FlowLayoutPanel();
			buttons.Dock = DockStyle.Fill;
			buttons.AutoSize = true;
			buttons.FlowDirection = FlowDirection.RightToLeft;
			buttons.WrapContents = false;
			buttons.Margin = new Padding(0);
			buttons.Controls.Add(m_btnClose);
			buttons.Controls.Add(m_btnSync);

			int bottomRowHeight = Math.Max(S(30), m_btnSync.PreferredSize.Height);

			TableLayoutPanel bottom = new TableLayoutPanel();
			bottom.Dock = DockStyle.Fill;
			bottom.ColumnCount = 2;
			bottom.RowCount = 2;
			bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, lineHeight + S(4)));
			bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, bottomRowHeight));
			bottom.Margin = new Padding(0);
			bottom.Controls.Add(m_lblStatus, 0, 0);
			bottom.SetColumnSpan(m_lblStatus, 2);
			bottom.Controls.Add(m_pbProgress, 0, 1);
			bottom.Controls.Add(buttons, 1, 1);

			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.ColumnCount = 1;
			root.RowCount = 5;
			root.Padding = new Padding(pad);
			root.BackColor = Color.Transparent;
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, lineHeight + gap));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(196)));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, lineHeight * 2 + gap));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, lineHeight + S(4) + bottomRowHeight));
			root.Controls.Add(m_lblSummary, 0, 0);
			root.Controls.Add(lists, 0, 1);
			root.Controls.Add(m_lblWarnings, 0, 2);
			root.Controls.Add(m_lvResults, 0, 3);
			root.Controls.Add(bottom, 0, 4);

			AcceptButton = m_btnSync;
			CancelButton = m_btnClose;

			Controls.Add(root);

			ResumeLayout(false);
			PerformLayout();
		}

		private void FillLists()
		{
			m_lblSummary.Text = "Entry \u201c" + m_model.EntryTitle + "\u201d \u2014 " +
				m_model.Repositories.Count.ToString() + " repositories \u00d7 " +
				m_model.Secrets.Count.ToString() + " secrets";

			foreach(string repository in m_model.Repositories)
				m_clbRepositories.Items.Add(repository, true);

			foreach(SecretField field in m_model.Secrets)
				m_clbSecrets.Items.Add(field, true);

			if(m_model.Warnings.Count > 0)
			{
				List<string> shown = new List<string>();
				for(int i = 0; (i < m_model.Warnings.Count) && (i < 3); ++i)
					shown.Add(m_model.Warnings[i]);
				string text = "Skipped: " + string.Join("  ", shown.ToArray());
				if(m_model.Warnings.Count > shown.Count)
					text += "  (+" + (m_model.Warnings.Count - shown.Count).ToString() + " more)";
				m_lblWarnings.Text = text;
			}

			if((m_model.Repositories.Count == 0) || (m_model.Secrets.Count == 0))
			{
				m_btnSync.Enabled = false;
				if(m_model.Secrets.Count == 0)
					m_lblStatus.Text = "No custom field to push as a secret.";
				else if(m_model.Repositories.Count == 0)
					m_lblStatus.Text = "No repository listed in \"" +
						SecretEntryModel.RepositoryField + "\".";
			}
		}

		private void OnSyncClick(object sender, EventArgs e)
		{
			if(m_bBusy) return;

			List<string> repositories = new List<string>();
			foreach(object item in m_clbRepositories.CheckedItems)
				repositories.Add((string)item);

			List<SecretField> secrets = new List<SecretField>();
			foreach(object item in m_clbSecrets.CheckedItems)
				secrets.Add((SecretField)item);

			if((repositories.Count == 0) || (secrets.Count == 0))
			{
				MessageBox.Show(this, "Select at least one repository and one secret.",
					Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			string ghPath = GhCli.FindExecutable();
			if(ghPath == null)
			{
				MessageBox.Show(this,
					"gh.exe (the GitHub CLI) was not found on PATH or in the Scoop shims.\r\n\r\n" +
					"Install it with \"scoop install gh\" and run \"gh auth login\" once.",
					Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			m_lvResults.Items.Clear();
			m_pbProgress.Minimum = 0;
			m_pbProgress.Maximum = repositories.Count * secrets.Count;
			m_pbProgress.Value = 0;
			m_lblStatus.Text = "Syncing\u2026";
			SetBusy(true);

			Thread worker = new Thread(delegate()
			{
				SyncWorker(repositories, secrets, ghPath);
			});
			worker.IsBackground = true;
			worker.Name = "KeePassGitHubSecrets Sync";
			worker.Start();
		}

		private void SyncWorker(List<string> repositories, List<SecretField> secrets, string ghPath)
		{
			Post(delegate() { m_lblStatus.Text = "Checking gh authentication\u2026"; });

			GhResult auth = GhCli.CheckAuth(ghPath);
			if(!auth.Success)
			{
				string message = auth.Message;
				Post(delegate()
				{
					m_lblStatus.Text = "Not authenticated.";
					SetBusy(false);
					MessageBox.Show(this, message, Text,
						MessageBoxButtons.OK, MessageBoxIcon.Error);
				});
				return;
			}

			int total = repositories.Count * secrets.Count;
			int done = 0;
			int succeeded = 0;
			int failed = 0;

			foreach(string repository in repositories)
			{
				foreach(SecretField field in secrets)
				{
					GhResult result;
					string value = null;
					try
					{
						value = m_model.ReadSecretValue(field);
						result = GhCli.SetSecret(ghPath, repository, field.Name, value);
					}
					catch(Exception ex)
					{
						result = new GhResult(false, ex.Message);
					}
					finally
					{
						value = null; // the string itself cannot be wiped; drop the reference
					}

					++done;
					if(result.Success) ++succeeded;
					else ++failed;

					string repositoryCopy = repository;
					string nameCopy = field.Name;
					string messageCopy = result.Message;
					bool successCopy = result.Success;
					int doneCopy = done;
					int succeededCopy = succeeded;
					int failedCopy = failed;

					Post(delegate()
					{
						ListViewItem item = new ListViewItem(repositoryCopy);
						item.SubItems.Add(nameCopy);
						item.SubItems.Add(successCopy ? "OK" : "FAILED");
						item.SubItems.Add(messageCopy);
						if(!successCopy) item.ForeColor = Color.Firebrick;
						m_lvResults.Items.Add(item);
						item.EnsureVisible();

						int next = m_pbProgress.Value + 1;
						m_pbProgress.Value = (next > m_pbProgress.Maximum)
							? m_pbProgress.Maximum : next;
						m_lblStatus.Text = doneCopy.ToString() + "/" + total.ToString() +
							" \u2014 " + succeededCopy.ToString() + " ok, " +
							failedCopy.ToString() + " failed";
					});
				}
			}

			Post(delegate()
			{
				SetBusy(false);
				m_lblStatus.Text = "Finished: " + succeeded.ToString() + " ok, " +
					failed.ToString() + " failed, " + total.ToString() + " calls.";
			});
		}

		private void SetBusy(bool busy)
		{
			m_bBusy = busy;
			m_btnSync.Enabled = !busy;
			m_btnClose.Enabled = !busy;
			m_clbRepositories.Enabled = !busy;
			m_clbSecrets.Enabled = !busy;
		}

		/// <summary>Marshals a call from the worker thread onto the UI thread.</summary>
		private void Post(MethodInvoker action)
		{
			if(IsDisposed) return;
			try
			{
				BeginInvoke(action);
			}
			catch(ObjectDisposedException) { }
			catch(InvalidOperationException) { }
		}
	}
}
