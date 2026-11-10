using CommunityToolkit.Mvvm.Input;
using Opti.Core.Software;

namespace Opti.ViewModels;

/// <summary>Рекомендуемые программы по категориям — только ссылки на официальные сайты.</summary>
public sealed partial class SoftwareViewModel : PageViewModel
{
    public SoftwareViewModel(MainViewModel main) : base(main) { }
    public override string Title => "Программы";
    public IReadOnlyList<SoftwareCategory> Categories => SoftwareCatalog.All;

    [RelayCommand] private void Open(SoftwareEntry e) => Main.Services.OpenPath(e.Url);
}
