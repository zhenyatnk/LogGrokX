using System;

namespace LogGrokX.Controls.GridView
{
    public partial class LogGridViewColumn 
    {
        public string PropertyName { get; } = "";

        public string? FieldName { get; set; }

        public bool FitToContent { get; set; }

        public Func<object, string>? ContentTextProvider { get; set; }

        internal LogGridViewColumn? RootExpanderHost { get; set; }

        public LogGridViewColumn()
        {
            InitializeComponent();
        }
    }
}
