
using ContentDistributionPlayer.Controls;

namespace ContentDistributionPlayer
{
    partial class MainForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.lblMessage = new System.Windows.Forms.Label();
            this.panScenesContentsContainer = new System.Windows.Forms.Panel();
            this.panLiveContentContainer = new System.Windows.Forms.Panel();
            this.imgPreload = new ContentDistributionPlayer.Controls.PictureBoxWithOpacity();
            this.imgBackgroundLogo = new ContentDistributionPlayer.Controls.PictureBoxWithOpacity();
            this.imgPresentationBackground = new ContentDistributionPlayer.Controls.PictureBoxWithOpacity();
            ((System.ComponentModel.ISupportInitialize)(this.imgPreload)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.imgBackgroundLogo)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.imgPresentationBackground)).BeginInit();
            this.SuspendLayout();
            // 
            // lblMessage
            // 
            this.lblMessage.BackColor = System.Drawing.Color.Transparent;
            this.lblMessage.Font = new System.Drawing.Font("Microsoft Sans Serif", 26.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblMessage.Location = new System.Drawing.Point(12, 574);
            this.lblMessage.Name = "lblMessage";
            this.lblMessage.Size = new System.Drawing.Size(477, 44);
            this.lblMessage.TabIndex = 1;
            this.lblMessage.Text = "Content Distribution initialization...";
            this.lblMessage.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // panScenesContentsContainer
            // 
            this.panScenesContentsContainer.Location = new System.Drawing.Point(556, 184);
            this.panScenesContentsContainer.Name = "panScenesContentsContainer";
            this.panScenesContentsContainer.Size = new System.Drawing.Size(200, 138);
            this.panScenesContentsContainer.TabIndex = 6;
            this.panScenesContentsContainer.Visible = false;
            // 
            // panLiveContentContainer
            // 
            this.panLiveContentContainer.Location = new System.Drawing.Point(556, 365);
            this.panLiveContentContainer.Name = "panLiveContentContainer";
            this.panLiveContentContainer.Size = new System.Drawing.Size(200, 142);
            this.panLiveContentContainer.TabIndex = 7;
            this.panLiveContentContainer.Visible = false;
            // 
            // imgPreload
            // 
            this.imgPreload.Image = global::ContentDistributionPlayer.Properties.Resources.preload;
            this.imgPreload.Location = new System.Drawing.Point(196, 510);
            this.imgPreload.Name = "imgPreload";
            this.imgPreload.Opacity = 1F;
            this.imgPreload.Size = new System.Drawing.Size(50, 50);
            this.imgPreload.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.imgPreload.TabIndex = 2;
            this.imgPreload.TabStop = false;
            // 
            // imgBackgroundLogo
            // 
            this.imgBackgroundLogo.BackColor = System.Drawing.Color.Transparent;
            this.imgBackgroundLogo.Image = global::ContentDistributionPlayer.Properties.Resources.logo;
            this.imgBackgroundLogo.Location = new System.Drawing.Point(126, 165);
            this.imgBackgroundLogo.Margin = new System.Windows.Forms.Padding(0);
            this.imgBackgroundLogo.Name = "imgBackgroundLogo";
            this.imgBackgroundLogo.Opacity = 1F;
            this.imgBackgroundLogo.Size = new System.Drawing.Size(200, 200);
            this.imgBackgroundLogo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.imgBackgroundLogo.TabIndex = 0;
            this.imgBackgroundLogo.TabStop = false;
            // 
            // imgPresentationBackground
            // 
            this.imgPresentationBackground.BackColor = System.Drawing.Color.White;
            this.imgPresentationBackground.Dock = System.Windows.Forms.DockStyle.Fill;
            this.imgPresentationBackground.Location = new System.Drawing.Point(0, 0);
            this.imgPresentationBackground.Name = "imgPresentationBackground";
            this.imgPresentationBackground.Opacity = 1F;
            this.imgPresentationBackground.Size = new System.Drawing.Size(871, 681);
            this.imgPresentationBackground.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.imgPresentationBackground.TabIndex = 5;
            this.imgPresentationBackground.TabStop = false;
            this.imgPresentationBackground.MouseClick += new System.Windows.Forms.MouseEventHandler(this.ImgPresentationBackground_MouseClick);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(871, 681);
            this.Controls.Add(this.panLiveContentContainer);
            this.Controls.Add(this.panScenesContentsContainer);
            this.Controls.Add(this.imgPreload);
            this.Controls.Add(this.lblMessage);
            this.Controls.Add(this.imgBackgroundLogo);
            this.Controls.Add(this.imgPresentationBackground);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "MainForm";
            this.Text = "Accenture Content Distribution";
            this.Activated += new System.EventHandler(this.MainForm_Activated);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.SizeChanged += new System.EventHandler(this.MainForm_SizeChanged);
            ((System.ComponentModel.ISupportInitialize)(this.imgPreload)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.imgBackgroundLogo)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.imgPresentationBackground)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion
        private PictureBoxWithOpacity imgBackgroundLogo;
        private System.Windows.Forms.Label lblMessage;
        private PictureBoxWithOpacity imgPreload;
        private System.Windows.Forms.Panel panScenesContentsContainer;
        private PictureBoxWithOpacity imgPresentationBackground;
        private System.Windows.Forms.Panel panLiveContentContainer;
    }
}

