using System.ComponentModel;

namespace SpellCounter
{
    public class SaveSettings
    {
        public int PogoCount = 0;
    }

    public class GlobalSettings
    {
        public bool ShowPogoCounter = true;
        public int PositionIndex = 0;
        public bool StrictMode = true;
        public int CounterType = 0;
        public int ResetMode = 0;
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}