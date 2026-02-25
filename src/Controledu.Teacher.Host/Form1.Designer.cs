#nullable disable
namespace Controledu.Teacher.Host;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;
    private Microsoft.Web.WebView2.WinForms.WebView2 webView = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            webView?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        webView = new Microsoft.Web.WebView2.WinForms.WebView2();
        ((System.ComponentModel.ISupportInitialize)webView).BeginInit();
        SuspendLayout();
        // 
        // webView
        // 
        webView.AllowExternalDrop = true;
        webView.CreationProperties = null;
        webView.DefaultBackgroundColor = System.Drawing.Color.White;
        webView.Dock = DockStyle.Fill;
        webView.Location = new System.Drawing.Point(0, 0);
        webView.Name = "webView";
        webView.Size = new System.Drawing.Size(1280, 800);
        webView.TabIndex = 0;
        webView.ZoomFactor = 1D;
        // 
        // Form1
        // 
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(1280, 800);
        Controls.Add(webView);
        Name = "Form1";
        Text = "Controledu Console";
        ((System.ComponentModel.ISupportInitialize)webView).EndInit();
        ResumeLayout(false);
    }
}
