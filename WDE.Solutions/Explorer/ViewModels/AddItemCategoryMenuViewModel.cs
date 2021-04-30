using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using AsyncAwaitBestPractices.MVVM;
using Prism.Events;
using Prism.Mvvm;
using WDE.Common;
using WDE.Common.Events;
using WDE.Common.Utils;

namespace WDE.Solutions.Explorer.ViewModels
{
    public class AddItemCategoryMenuViewModel : BindableBase
    {
        public string Name { get; }
        public ObservableCollection<SolutionItemMenuViewModel> Items { get; } = new();

        public AddItemCategoryMenuViewModel(string groupName)
        {
            Name = groupName;
        }
    }

    public class SolutionItemMenuViewModel : BindableBase
    {
        public string Name { get; }
        public ICommand Command { get; }
        
        public SolutionItemMenuViewModel(ISolutionItemProvider provider, 
            Func<ISolutionItemProvider, Task> addCommand)
        {
            Name = provider.GetName();

            Command = new AsyncCommand(async () =>
            {
                await addCommand.Invoke(provider);
            });
        }
    }
}