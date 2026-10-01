using System;

namespace LogGrokX.Controls.GridView
{
    public partial class LogGridViewColumn 
    {
        public string PropertyName { get; } = "";

        public bool FitToContent { get; set; }

        public Func<object, string>? ContentTextProvider { get; set; }

        public LogGridViewColumn()
        {
            InitializeComponent();
        }
    }
}
