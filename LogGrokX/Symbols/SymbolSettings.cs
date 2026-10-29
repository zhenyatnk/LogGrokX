namespace LogGrokX.Symbols;

public class SymbolSettings
{
    public bool Enabled { get; set; } = true;

    // WinDbg syntax, e.g. "srv*C:\Symbols*https://symbols.example.com;C:\Build\Pdb". Empty uses _NT_SYMBOL_PATH.
    public string SymbolPath { get; set; } = string.Empty;

    // Local source roots separated by ';'; "srv*" enables the source server data of the PDB files.
    public string SourcePath { get; set; } = "srv*";

    // Downstream store for downloaded PDB and source files. Empty uses %LOCALAPPDATA%\LogGrokX\Symbols.
    public string CachePath { get; set; } = string.Empty;

    // Allows running the extraction commands of the source server data (tf.exe, git, ...).
    public bool AllowSourceServerCommands { get; set; }
}
