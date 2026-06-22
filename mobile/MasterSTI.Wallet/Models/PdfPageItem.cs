using System.ComponentModel;
using System.Runtime.CompilerServices;
using MasterSTI.Wallet.Services;

namespace MasterSTI.Wallet.Models;

/// <summary>
/// One page of the ReviewPage PDF preview. The bitmap is rendered lazily the
/// first time its CollectionView container binds to this item, so memory tracks
/// the visible scroll window rather than the whole document.
/// </summary>
public sealed class PdfPageItem : INotifyPropertyChanged
{
    private readonly IPdfDocumentSession _session;
    private readonly int _index;
    private ImageSource? _source;
    private bool _rendering;
    private bool _rendered;

    public PdfPageItem(IPdfDocumentSession session, int index, int totalPages)
    {
        _session = session;
        _index = index;
        TotalPages = totalPages;
    }

    public int PageNumber => _index + 1;
    public int TotalPages { get; }
    public string Caption => $"PAGINA {PageNumber} DIN {TotalPages}";

    public ImageSource? Source
    {
        get => _source;
        private set
        {
            _source = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLoading));
        }
    }

    public bool IsLoading => _source is null;

    public async Task EnsureRenderedAsync()
    {
        if (_rendered || _rendering) return;
        _rendering = true;
        try
        {
            var img = await _session.RenderPageAsync(_index);
            MainThread.BeginInvokeOnMainThread(() => Source = img);
            _rendered = img is not null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PdfPageItem] render page {_index} failed: {ex}");
        }
        finally
        {
            _rendering = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
