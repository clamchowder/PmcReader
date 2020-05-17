namespace PmcReader
{
    partial class HaswellForm
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
            this.configSelect = new System.Windows.Forms.ListView();
            this.applyConfigButton = new System.Windows.Forms.Button();
            this.monitoringListView = new System.Windows.Forms.ListView();
            this.cpuidLabel = new System.Windows.Forms.Label();
            this.configListLabel = new System.Windows.Forms.Label();
            this.errorLabel = new System.Windows.Forms.Label();
            this.L3ConfigSelect = new System.Windows.Forms.ListView();
            this.L3CacheConfigLabel = new System.Windows.Forms.Label();
            this.applyL3ConfigButton = new System.Windows.Forms.Button();
            this.L3MonitoringListView = new System.Windows.Forms.ListView();
            this.dfConfigSelect = new System.Windows.Forms.ListView();
            this.DataFabricConfigLabel = new System.Windows.Forms.Label();
            this.button1 = new System.Windows.Forms.Button();
            this.dfMonitoringListView = new System.Windows.Forms.ListView();
            this.l3ErrorMessage = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // configSelect
            // 
            this.configSelect.HideSelection = false;
            this.configSelect.Location = new System.Drawing.Point(12, 34);
            this.configSelect.MultiSelect = false;
            this.configSelect.Name = "configSelect";
            this.configSelect.Size = new System.Drawing.Size(1023, 85);
            this.configSelect.TabIndex = 1;
            this.configSelect.UseCompatibleStateImageBehavior = false;
            this.configSelect.View = System.Windows.Forms.View.List;
            this.configSelect.SelectedIndexChanged += new System.EventHandler(this.listView1_SelectedIndexChanged);
            // 
            // applyConfigButton
            // 
            this.applyConfigButton.Location = new System.Drawing.Point(12, 125);
            this.applyConfigButton.Name = "applyConfigButton";
            this.applyConfigButton.Size = new System.Drawing.Size(75, 23);
            this.applyConfigButton.TabIndex = 2;
            this.applyConfigButton.Text = "Apply Config";
            this.applyConfigButton.UseVisualStyleBackColor = true;
            this.applyConfigButton.Click += new System.EventHandler(this.applyConfigButton_Click);
            // 
            // monitoringListView
            // 
            this.monitoringListView.HideSelection = false;
            this.monitoringListView.Location = new System.Drawing.Point(12, 154);
            this.monitoringListView.Name = "monitoringListView";
            this.monitoringListView.Size = new System.Drawing.Size(1023, 284);
            this.monitoringListView.TabIndex = 3;
            this.monitoringListView.UseCompatibleStateImageBehavior = false;
            this.monitoringListView.View = System.Windows.Forms.View.Details;
            // 
            // cpuidLabel
            // 
            this.cpuidLabel.AutoSize = true;
            this.cpuidLabel.Location = new System.Drawing.Point(9, 5);
            this.cpuidLabel.Name = "cpuidLabel";
            this.cpuidLabel.Size = new System.Drawing.Size(35, 13);
            this.cpuidLabel.TabIndex = 4;
            this.cpuidLabel.Text = "label1";
            // 
            // configListLabel
            // 
            this.configListLabel.AutoSize = true;
            this.configListLabel.Location = new System.Drawing.Point(9, 18);
            this.configListLabel.Name = "configListLabel";
            this.configListLabel.Size = new System.Drawing.Size(178, 13);
            this.configListLabel.TabIndex = 5;
            this.configListLabel.Text = "Core PMC Configurations (pick one):";
            // 
            // errorLabel
            // 
            this.errorLabel.AutoSize = true;
            this.errorLabel.Location = new System.Drawing.Point(94, 135);
            this.errorLabel.Name = "errorLabel";
            this.errorLabel.Size = new System.Drawing.Size(0, 13);
            this.errorLabel.TabIndex = 6;
            // 
            // L3ConfigSelect
            // 
            this.L3ConfigSelect.HideSelection = false;
            this.L3ConfigSelect.Location = new System.Drawing.Point(13, 457);
            this.L3ConfigSelect.Name = "L3ConfigSelect";
            this.L3ConfigSelect.Size = new System.Drawing.Size(681, 63);
            this.L3ConfigSelect.TabIndex = 7;
            this.L3ConfigSelect.UseCompatibleStateImageBehavior = false;
            this.L3ConfigSelect.View = System.Windows.Forms.View.List;
            // 
            // L3CacheConfigLabel
            // 
            this.L3CacheConfigLabel.AutoSize = true;
            this.L3CacheConfigLabel.Location = new System.Drawing.Point(10, 441);
            this.L3CacheConfigLabel.Name = "L3CacheConfigLabel";
            this.L3CacheConfigLabel.Size = new System.Drawing.Size(202, 13);
            this.L3CacheConfigLabel.TabIndex = 8;
            this.L3CacheConfigLabel.Text = "L3 Cache PMC Configurations (pick one):";
            // 
            // applyL3ConfigButton
            // 
            this.applyL3ConfigButton.Location = new System.Drawing.Point(13, 527);
            this.applyL3ConfigButton.Name = "applyL3ConfigButton";
            this.applyL3ConfigButton.Size = new System.Drawing.Size(94, 23);
            this.applyL3ConfigButton.TabIndex = 9;
            this.applyL3ConfigButton.Text = "Apply L3 Config";
            this.applyL3ConfigButton.UseVisualStyleBackColor = true;
            this.applyL3ConfigButton.Click += new System.EventHandler(this.applyL3ConfigButton_Click);
            // 
            // L3MonitoringListView
            // 
            this.L3MonitoringListView.HideSelection = false;
            this.L3MonitoringListView.Location = new System.Drawing.Point(12, 557);
            this.L3MonitoringListView.Name = "L3MonitoringListView";
            this.L3MonitoringListView.Size = new System.Drawing.Size(682, 118);
            this.L3MonitoringListView.TabIndex = 10;
            this.L3MonitoringListView.UseCompatibleStateImageBehavior = false;
            this.L3MonitoringListView.View = System.Windows.Forms.View.Details;
            // 
            // dfConfigSelect
            // 
            this.dfConfigSelect.HideSelection = false;
            this.dfConfigSelect.Location = new System.Drawing.Point(700, 457);
            this.dfConfigSelect.Name = "dfConfigSelect";
            this.dfConfigSelect.Size = new System.Drawing.Size(335, 63);
            this.dfConfigSelect.TabIndex = 11;
            this.dfConfigSelect.UseCompatibleStateImageBehavior = false;
            this.dfConfigSelect.View = System.Windows.Forms.View.List;
            // 
            // DataFabricConfigLabel
            // 
            this.DataFabricConfigLabel.AutoSize = true;
            this.DataFabricConfigLabel.Location = new System.Drawing.Point(697, 441);
            this.DataFabricConfigLabel.Name = "DataFabricConfigLabel";
            this.DataFabricConfigLabel.Size = new System.Drawing.Size(211, 13);
            this.DataFabricConfigLabel.TabIndex = 12;
            this.DataFabricConfigLabel.Text = "Data Fabric PMC Configurations (pick one):";
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(700, 528);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(102, 23);
            this.button1.TabIndex = 13;
            this.button1.Text = "Apply DF Config";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.applyDfConfigButton_Click);
            // 
            // dfMonitoringListView
            // 
            this.dfMonitoringListView.HideSelection = false;
            this.dfMonitoringListView.Location = new System.Drawing.Point(700, 557);
            this.dfMonitoringListView.Name = "dfMonitoringListView";
            this.dfMonitoringListView.Size = new System.Drawing.Size(335, 118);
            this.dfMonitoringListView.TabIndex = 14;
            this.dfMonitoringListView.UseCompatibleStateImageBehavior = false;
            this.dfMonitoringListView.View = System.Windows.Forms.View.Details;
            // 
            // l3ErrorMessage
            // 
            this.l3ErrorMessage.AutoSize = true;
            this.l3ErrorMessage.Location = new System.Drawing.Point(114, 536);
            this.l3ErrorMessage.Name = "l3ErrorMessage";
            this.l3ErrorMessage.Size = new System.Drawing.Size(0, 13);
            this.l3ErrorMessage.TabIndex = 15;
            // 
            // HaswellForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1047, 687);
            this.Controls.Add(this.l3ErrorMessage);
            this.Controls.Add(this.dfMonitoringListView);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.DataFabricConfigLabel);
            this.Controls.Add(this.dfConfigSelect);
            this.Controls.Add(this.L3MonitoringListView);
            this.Controls.Add(this.applyL3ConfigButton);
            this.Controls.Add(this.L3CacheConfigLabel);
            this.Controls.Add(this.L3ConfigSelect);
            this.Controls.Add(this.errorLabel);
            this.Controls.Add(this.configListLabel);
            this.Controls.Add(this.cpuidLabel);
            this.Controls.Add(this.monitoringListView);
            this.Controls.Add(this.applyConfigButton);
            this.Controls.Add(this.configSelect);
            this.Name = "HaswellForm";
            this.Text = "CPU Performance Monitoring";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.ListView configSelect;
        private System.Windows.Forms.Button applyConfigButton;
        private System.Windows.Forms.ListView monitoringListView;
        private System.Windows.Forms.Label cpuidLabel;
        private System.Windows.Forms.Label configListLabel;
        private System.Windows.Forms.Label errorLabel;
        private System.Windows.Forms.ListView L3ConfigSelect;
        private System.Windows.Forms.Label L3CacheConfigLabel;
        private System.Windows.Forms.Button applyL3ConfigButton;
        private System.Windows.Forms.ListView L3MonitoringListView;
        private System.Windows.Forms.ListView dfConfigSelect;
        private System.Windows.Forms.Label DataFabricConfigLabel;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.ListView dfMonitoringListView;
        private System.Windows.Forms.Label l3ErrorMessage;
    }
}

