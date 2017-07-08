namespace IStorage_Builder
{
    partial class IStorageBuilder
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.B_Open = new System.Windows.Forms.Button();
            this.B_Go = new System.Windows.Forms.Button();
            this.TB_Path = new System.Windows.Forms.TextBox();
            this.TB_Progress = new System.Windows.Forms.TextBox();
            this.PB_Show = new System.Windows.Forms.ProgressBar();
            this.SuspendLayout();
            // 
            // B_Open
            // 
            this.B_Open.Location = new System.Drawing.Point(311, 16);
            this.B_Open.Name = "B_Open";
            this.B_Open.Size = new System.Drawing.Size(106, 22);
            this.B_Open.TabIndex = 0;
            this.B_Open.Text = "Open";
            this.B_Open.UseVisualStyleBackColor = true;
            this.B_Open.Click += new System.EventHandler(this.B_Open_Click);
            // 
            // B_Go
            // 
            this.B_Go.Location = new System.Drawing.Point(311, 44);
            this.B_Go.Name = "B_Go";
            this.B_Go.Size = new System.Drawing.Size(106, 22);
            this.B_Go.TabIndex = 1;
            this.B_Go.Text = "Go";
            this.B_Go.UseVisualStyleBackColor = true;
            this.B_Go.Click += new System.EventHandler(this.B_Go_Click);
            // 
            // TB_Path
            // 
            this.TB_Path.Location = new System.Drawing.Point(3, 17);
            this.TB_Path.Name = "TB_Path";
            this.TB_Path.ReadOnly = true;
            this.TB_Path.Size = new System.Drawing.Size(306, 20);
            this.TB_Path.TabIndex = 2;
            // 
            // TB_Progress
            // 
            this.TB_Progress.Location = new System.Drawing.Point(5, 48);
            this.TB_Progress.Multiline = true;
            this.TB_Progress.Name = "TB_Progress";
            this.TB_Progress.ReadOnly = true;
            this.TB_Progress.Size = new System.Drawing.Size(303, 179);
            this.TB_Progress.TabIndex = 4;
            // 
            // PB_Show
            // 
            this.PB_Show.Location = new System.Drawing.Point(6, 235);
            this.PB_Show.Name = "PB_Show";
            this.PB_Show.Size = new System.Drawing.Size(410, 23);
            this.PB_Show.TabIndex = 5;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(429, 264);
            this.Controls.Add(this.PB_Show);
            this.Controls.Add(this.TB_Progress);
            this.Controls.Add(this.TB_Path);
            this.Controls.Add(this.B_Go);
            this.Controls.Add(this.B_Open);
            this.Name = "Form1";
            this.Text = "IStorage Builder";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button B_Open;
        private System.Windows.Forms.Button B_Go;
        private System.Windows.Forms.TextBox TB_Path;
        private System.Windows.Forms.TextBox TB_Progress;
        private System.Windows.Forms.ProgressBar PB_Show;
    }
}

