using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using PlayTimeTracker.Core;

namespace PlayTimeTracker.App;

/// <summary>감지하지 않을 exe 이름 목록을 보고 고친다. 추가/빼기는 바로 적용된다.</summary>
public partial class ExclusionWindow : Window
{
    private readonly TrackingEngine _engine;
    private readonly ObservableCollection<string> _names = new();

    public ExclusionWindow(TrackingEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        Icon = AppIcon.Window;
        UiScale.Attach(this);
        NameList.ItemsSource = _names;
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        _names.Clear();
        foreach (var name in _engine.Db.GetExclusions()) _names.Add(name);
        StatusText.Text = $"{_names.Count}개";
    }

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Add();
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => Add();

    private void Add()
    {
        string? name = ExclusionList.NormalizeName(NameBox.Text);
        if (name == null) return;

        if (_names.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            StatusText.Text = $"{name}은(는) 이미 목록에 있습니다.";
            return;
        }

        // 이미 기록된 프로그램이면 그 기록도 함께 지워지므로 먼저 확인한다
        var recorded = _engine.FindByExeName(name);
        if (recorded.Count > 0)
        {
            var answer = MessageBox.Show(this,
                $"{name}은(는) 이미 기록된 프로그램입니다. 제외 목록에 추가하면 지금까지의 기록도 DB에서 완전히 삭제됩니다.\n\n삭제한 기록은 되돌릴 수 없습니다. 계속할까요?",
                "제외 목록에 추가", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }

        _engine.AddExclusion(name);
        NameBox.Clear();
        Load();
        StatusText.Text = recorded.Count > 0
            ? $"{name}을(를) 추가하고 기록을 삭제했습니다. 이제 감지하지 않습니다. ({_names.Count}개)"
            : $"{name}을(를) 추가했습니다. 이제 감지하지 않습니다. ({_names.Count}개)";
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not string name) return;

        _engine.RemoveExclusion(name);
        Load();
        StatusText.Text = $"{name}을(를) 뺐습니다. 다음 감지부터 기록합니다. ({_names.Count}개)";
    }
}
