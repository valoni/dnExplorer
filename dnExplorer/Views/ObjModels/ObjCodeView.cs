﻿using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using dnExplorer.Controls;
using dnExplorer.Language;
using dnExplorer.Models;
using dnlib.DotNet;
using ScintillaNET;

namespace dnExplorer.Views {
	public class ObjCodeView : ViewBase<ObjModel> {
		CodeView view;

		public ObjCodeView() {
			view = new CodeView();
			view.Navigate += OnNavigateTarget;
			view.NativeInterface.SetMouseDwellTime(200);
			view.DwellStart += DwellStart;
			view.MouseMove += MouseMoved;
			Controls.Add(view);
		}

		object sync = new object();
		CancellationTokenSource cancellation;
		ResponsiveOperation<CodeViewData> op;

		protected override void OnModelUpdated() {
			InitApp();
			GenerateCode();
		}

		bool appInited;

		void InitApp() {
			if (appInited)
				return;

			App.Languages.PropertyChanged += (sender, e) => GenerateCode();
			appInited = true;
		}

		void GenerateCode() {
			if (InvokeRequired) {
				Invoke(new Action(GenerateCode));
				return;
			}

			if (op != null) {
				op.Cancel();
				cancellation.Cancel();
			}

			if (Model == null) {
				view.Clear();
			}
			else {
				cancellation = new CancellationTokenSource();
				var state = new RunState(Model.Definition, App.Languages.ActiveLanguage, cancellation.Token);
				op = new ResponsiveOperation<CodeViewData>(state.Run);
				op.LoadingThreshold = 50;
				op.Completed += OnCompleted;
				op.Loading += OnLoading;
				op.Begin();
			}
		}

		struct RunState {
			IDnlibDef item;
			ILanguage lang;
			CancellationToken token;

			public RunState(IDnlibDef item, ILanguage lang, CancellationToken token) {
				this.item = item;
				this.lang = lang;
				this.token = token;
			}

			public CodeViewData Run() {
				try {
					return lang.Run(item, token);
				}
				catch (Exception ex) {
					return new CodeViewData(string.Format("Error occured in decompiling:{0}{1}", Environment.NewLine, ex));
				}
			}
		}

		void OnLoading(object sender, EventArgs e) {
			view.SetPlainText("Decompiling...");
		}

		void OnCompleted(object sender, OperationResultEventArgs<CodeViewData> e) {
			view.SetData(e.Result);
			op = null;
			cancellation = null;
		}

		void OnNavigateTarget(object sender, CodeViewNavigateEventArgs e) {
			if (!e.IsLocal) {
				App.Modules.NavigateTarget(e.Target);
			}
			else if (!e.IsDefinition)
				NavigateLocal(e.Target);
		}

		void NavigateLocal(object target) {
			foreach (var textRef in view.Data.References) {
				if (textRef.Value.Reference.Equals(target) && textRef.Value.IsDefinition) {
					new Range(textRef.Key, textRef.Key + textRef.Value.Length, view).Select();
					return;
				}
			}
			MessageBox.Show("Cannot find definition of '" + target + "'.", App.AppName, MessageBoxButtons.OK,
				MessageBoxIcon.Error);
		}

		int? hoverPos;
		ToolTip toolTip = new ToolTip { UseFading = false, ShowAlways = false };

		void DwellStart(object sender, ScintillaMouseEventArgs e) {
			if (view.Data == null || hoverPos != null) return;

			int txtPos = view.PositionFromPointClose(e.X, e.Y);
			CodeViewData.TextRef? textRef;
			if (txtPos != -1 && (textRef = view.ResolveReference(ref txtPos)) != null && textRef.Value.Reference is IFullName) {
				var line = view.Lines.FromPosition(txtPos);

				var text = DisplayNameCreator.CreateFullName((IFullName)textRef.Value.Reference);
				text = Utils.EscapeString(text, false);

				var pt = PointToClient(Cursor.Position);
				pt.X += 16;
				pt.Y += 10;

				toolTip.Show(text, this, pt);
				hoverPos = txtPos;

				view.Capture = true;
			}
		}

		void MouseMoved(object sender, MouseEventArgs e) {
			if (hoverPos != null) {
				var pt = view.PointToClient(PointToScreen(e.Location));
				int txtPos = view.PositionFromPointClose(pt.X, pt.Y);
				CodeViewData.TextRef? textRef;
				if (txtPos != -1 && (textRef = view.ResolveReference(ref txtPos)) != null) {
					if (txtPos == hoverPos)
						return;
				}

				toolTip.Hide(this);
				hoverPos = null;
				view.Capture = false;
			}
		}

		ContextMenuStrip ctxMenu;

		protected internal override ContextMenuStrip GetContextMenu() {
			if (ctxMenu != null)
				return ctxMenu;

			ctxMenu = new ContextMenuStrip();

			var gotoMD = new ToolStripMenuItem("Go To MetaData View");
			gotoMD.Click += GotoMD;
			ctxMenu.Items.Add(gotoMD);

			ctxMenu.Items.Add(new ToolStripSeparator());
			var analyze = new ToolStripMenuItem("Analyze", Resources.GetResource<Image>("Icons.search.png"));
			analyze.Click += Analyze;
			ctxMenu.Items.Add(analyze);

			return ctxMenu;
		}

		void GotoMD(object sender, EventArgs e) {
			var model = sender.GetContextMenuModel<ObjModel>();
			var module = model.Definition as ModuleDefMD;
			if (module == null)
				module = (ModuleDefMD)((IMemberDef)model.Definition).Module;
			ViewUtils.ShowToken(App, model, module.MetaData.PEImage, model.Definition.MDToken);
		}

		void Analyze(object sender, EventArgs e) {
			var model = sender.GetContextMenuModel<ObjModel>();
			App.Analyzer.Display(model.Definition);
		}
	}
}