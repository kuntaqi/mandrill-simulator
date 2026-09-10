using System.ComponentModel;

namespace MandrillSimulator.Models;

public class EndpointHit : INotifyPropertyChanged
{
    private int _hits;

    public EndpointHit(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public int Hits
    {
        get => _hits;
        set
        {
            if (_hits == value) return;
            _hits = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Hits)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
