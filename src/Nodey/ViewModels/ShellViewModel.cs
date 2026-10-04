namespace Nodey.ViewModels;

/// <summary>
/// Top-level shell state: which view is active. The shared document model
/// (save/load, undo/redo) lands here in STEP-6.
/// </summary>
public class ShellViewModel : ObservableObject
{
    private bool _isNodeViewActive = true;
    private bool _isDrawViewActive;

    public ShellViewModel()
    {
        SelectNodeCommand = new RelayCommand(SelectNode);
        SelectDrawCommand = new RelayCommand(SelectDraw);
    }

    /// <summary>True while the Node View tab is active.</summary>
    public bool IsNodeViewActive
    {
        get => _isNodeViewActive;
        set => SetAndRaise(ref _isNodeViewActive, value);
    }

    /// <summary>True while the Draw View tab is active.</summary>
    public bool IsDrawViewActive
    {
        get => _isDrawViewActive;
        set => SetAndRaise(ref _isDrawViewActive, value);
    }

    public RelayCommand SelectNodeCommand { get; }
    public RelayCommand SelectDrawCommand { get; }

    private void SelectNode() => SetActive(isNode: true, isDraw: false);

    private void SelectDraw() => SetActive(isNode: false, isDraw: true);

    private void SetActive(bool isNode, bool isDraw)
    {
        IsNodeViewActive = isNode;
        IsDrawViewActive = isDraw;
    }
}
