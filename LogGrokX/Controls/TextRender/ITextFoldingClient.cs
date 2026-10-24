namespace LogGrokX.Controls.TextRender;

public interface ITextFoldingClient
{
    void ResetFoldingToDefault();
    void OnSharedFoldingStateChanged();
}
