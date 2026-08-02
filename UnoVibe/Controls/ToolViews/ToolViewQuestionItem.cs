using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using UnoVibe.Models;
    using QuickMarkup.WinUI;
    QuestionFormItem Q;
    <StackPanel Spacing=4>
        <TextBlock Text=`Q.Question` FontSize=12 FontWeight=`FontWeights.SemiBold` TextWrapping=Wrap IsTextSelectionEnabled=true />
        if (`Q.Multiple`)
        {
            foreach (var opt in `Q.Options`)
                <CheckBox Content=`opt.Label` Tag=`opt` IsChecked=`opt.IsSelected` IsChecked+=>`v => opt.IsSelected = v ?? false` />
        }
        else
        {
            foreach (var opt in `Q.Options`)
                <RadioButton Content=`opt.Label` GroupName=`Q.Header` Tag=`opt` IsChecked=`opt.IsSelected` IsChecked+=>`v => opt.IsSelected = v ?? false` />
        }
        if (`Q.AllowCustom`)
            <TextBox Text<=>`Q.CustomText` PlaceholderText="Type a custom answer..." AcceptsReturn=true TextWrapping=Wrap />
    </StackPanel>
    """)]
public partial class ToolViewQuestionItem : IQuickMarkupComponent;
