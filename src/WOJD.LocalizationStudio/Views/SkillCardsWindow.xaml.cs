using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio.Views;

public partial class SkillCardsWindow : Window
{
    private readonly MainViewModel _mainViewModel;
    private readonly LocalizationDocument _document;
    private SkillCardLinkState _linkState;
    private List<SkillCardModel> _allCards = [];
    private SkillCardModel? _selectedCard;
    private SkillCardEntryView? _selectedEntry;
    private bool _updatingEditor;
    private string? _preferredSkillId;

    public SkillCardsWindow(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
        _document =
            mainViewModel.ActiveDocument
            ?? throw new InvalidOperationException(
                "Для карточек навыков сначала нужно открыть файл локализации.");

        _linkState =
            SkillCardLinkStore.Load(_document.FilePath);

        _preferredSkillId =
            SkillCardService.TryExtractSkillId(
                mainViewModel.SelectedEntry?.Key ?? string.Empty);

        InitializeComponent();
        RebuildCards(_preferredSkillId);
    }

    private void RebuildCards(string? preferredSkillId = null)
    {
        _allCards =
            SkillCardService.Build(
                _document,
                _linkState)
            .ToList();

        _preferredSkillId =
            preferredSkillId
            ?? _selectedCard?.Id
            ?? _preferredSkillId;

        ApplySearch();

        StatusText.Text =
            $"Источник: {System.IO.Path.GetFileName(_document.FilePath)} · найдено навыков: {_allCards.Count:N0}";
    }

    private void ApplySearch()
    {
        var query =
            SkillSearchBox?.Text?.Trim()
            ?? string.Empty;

        var filtered =
            string.IsNullOrWhiteSpace(query)
                ? _allCards
                : _allCards
                    .Where(x =>
                        x.Id.Contains(
                            query,
                            StringComparison.OrdinalIgnoreCase) ||
                        x.DisplayName.Contains(
                            query,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

        SkillList.ItemsSource = filtered;
        SkillCountText.Text =
            $"{filtered.Count:N0} из {_allCards.Count:N0}";

        SkillCardModel? target = null;

        if (!string.IsNullOrWhiteSpace(_preferredSkillId))
        {
            target =
                filtered.FirstOrDefault(x =>
                    string.Equals(
                        x.Id,
                        _preferredSkillId,
                        StringComparison.OrdinalIgnoreCase));
        }

        target ??=
            filtered.FirstOrDefault();

        if (target is not null)
        {
            SkillList.SelectedItem = target;
            SkillList.ScrollIntoView(target);
        }
        else
        {
            ShowEmptyCard();
        }
    }

    private void SkillSearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (!IsInitialized)
            return;

        _preferredSkillId = null;
        ApplySearch();
    }

    private void SkillList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (SkillList.SelectedItem is not SkillCardModel card)
        {
            ShowEmptyCard();
            return;
        }

        _selectedCard = card;
        _preferredSkillId = card.Id;

        SkillTitleText.Text = card.DisplayName;
        SkillIdText.Text = card.Id;
        UniqueCountText.Text = card.UniqueCount.ToString("N0");
        SharedCountText.Text = card.SharedCount.ToString("N0");
        LinkedCountText.Text = card.LinkedLabel;
        EntryCountText.Text = $"{card.LinkedCount:N0} строк";
        SkillDescriptionText.Text = card.DescriptionPreview;

        EntryList.ItemsSource = card.Entries;

        var target =
            card.NameEntry
            ?? card.Entries.FirstOrDefault();

        EntryList.SelectedItem = target;

        if (target is not null)
            EntryList.ScrollIntoView(target);
    }

    private void EntryList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _selectedEntry =
            EntryList.SelectedItem as SkillCardEntryView;

        UpdateEditor();
    }

    private void UpdateEditor()
    {
        _updatingEditor = true;

        try
        {
            if (_selectedEntry is null)
            {
                RoleText.Text = "Выберите связанную строку";
                LinkTypeText.Text = string.Empty;
                NamespaceBox.Text = string.Empty;
                KeyBox.Text = string.Empty;
                OriginalBox.Text = string.Empty;
                TranslationBox.Text = string.Empty;
                QaText.Text = string.Empty;
                CharacterCountText.Text = string.Empty;
                return;
            }

            var entry = _selectedEntry.Entry;

            RoleText.Text = _selectedEntry.Role;
            LinkTypeText.Text =
                _selectedEntry.IsCustom
                    ? $"{_selectedEntry.LinkTypeText} · добавлен вручную"
                    : _selectedEntry.LinkTypeText;
            NamespaceBox.Text = entry.Namespace;
            KeyBox.Text = entry.Key;
            OriginalBox.Text = entry.Original;
            TranslationBox.Text = entry.Translation;
            CharacterCountText.Text = $"{entry.CharacterCount:N0} символов";
            QaText.Text =
                entry.HasValidationIssues
                    ? entry.ValidationSummary
                    : "✓ Ошибок нет";
        }
        finally
        {
            _updatingEditor = false;
        }
    }

    private void TranslationBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_updatingEditor ||
            _selectedEntry is null)
        {
            return;
        }

        _selectedEntry.Entry.Translation =
            TranslationBox.Text;

        CharacterCountText.Text =
            $"{_selectedEntry.Entry.CharacterCount:N0} символов";

        QaText.Text =
            _selectedEntry.Entry.HasValidationIssues
                ? _selectedEntry.Entry.ValidationSummary
                : "✓ Ошибок нет";

        EntryList.Items.Refresh();
        SkillList.Items.Refresh();
    }

    private void AddLink_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedCard is null)
            return;

        var dialog =
            new TextInputDialog(
                "Добавить связь",
                "Введите Key или Namespace:Key существующей строки. Она будет показана в карточке навыка без копирования данных.")
            {
                Owner = this
            };

        if (dialog.ShowDialog() != true)
            return;

        var value = dialog.Value.Trim();

        if (string.IsNullOrWhiteSpace(value))
            return;

        string? entryNamespace = null;
        var key = value;

        var separator = value.IndexOf(':');

        if (separator > 0)
        {
            entryNamespace = value[..separator].Trim();
            key = value[(separator + 1)..].Trim();
        }

        var matches =
            _document.Entries
                .Where(x =>
                    string.Equals(
                        x.Key,
                        key,
                        StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(entryNamespace) ||
                     string.Equals(
                         x.Namespace,
                         entryNamespace,
                         StringComparison.OrdinalIgnoreCase)))
                .ToList();

        if (matches.Count == 0)
        {
            AppDialog.Show(
                $"Ключ «{value}» не найден в текущем файле.",
                "Карточки навыков",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                this);
            return;
        }

        if (matches.Count > 1 &&
            string.IsNullOrWhiteSpace(entryNamespace))
        {
            AppDialog.Show(
                "Найдено несколько строк с таким Key. Укажите Namespace:Key.",
                "Карточки навыков",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                this);
            return;
        }

        var entry = matches[0];

        if (!SkillCardLinkStore.AddLink(
                _linkState,
                _selectedCard.Id,
                entry))
        {
            AppDialog.Show(
                "Эта строка уже связана с карточкой.",
                "Карточки навыков",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                this);
            return;
        }

        SkillCardLinkStore.Save(
            _document.FilePath,
            _linkState);

        var skillId = _selectedCard.Id;
        RebuildCards(skillId);

        var rebuilt =
            _allCards.FirstOrDefault(x => x.Id == skillId);

        var linked =
            rebuilt?.Entries.FirstOrDefault(x =>
                string.Equals(
                    x.Entry.Namespace,
                    entry.Namespace,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    x.Entry.Key,
                    entry.Key,
                    StringComparison.OrdinalIgnoreCase));

        if (linked is not null)
            EntryList.SelectedItem = linked;
    }

    private void RemoveLink_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedCard is null ||
            _selectedEntry is null)
        {
            return;
        }

        if (!_selectedEntry.IsCustom)
        {
            AppDialog.Show(
                "Эта связь найдена автоматически и не хранится как ручная. Удалять её из карточки не требуется.",
                "Карточки навыков",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                this);
            return;
        }

        var entry = _selectedEntry.Entry;
        var skillId = _selectedCard.Id;

        if (SkillCardLinkStore.RemoveLink(
                _linkState,
                skillId,
                entry))
        {
            SkillCardLinkStore.Save(
                _document.FilePath,
                _linkState);
            RebuildCards(skillId);
        }
    }

    private void OpenInFile_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedEntry is null)
            return;

        var entry = _selectedEntry.Entry;

        _mainViewModel.GoTo(
            $"{entry.Namespace}:{entry.Key}");

        Close();

        if (Owner is not null)
        {
            Owner.Activate();
            Owner.Focus();
        }
    }

    private void RefreshCards_Click(
        object sender,
        RoutedEventArgs e)
    {
        _linkState =
            SkillCardLinkStore.Load(_document.FilePath);

        RebuildCards(_selectedCard?.Id);
    }

    private void ShowEmptyCard()
    {
        _selectedCard = null;
        _selectedEntry = null;

        SkillTitleText.Text = "Навыки не найдены";
        SkillIdText.Text = string.Empty;
        UniqueCountText.Text = "0";
        SharedCountText.Text = "0";
        LinkedCountText.Text = "0 связанных ключей";
        EntryCountText.Text = "0 строк";
        SkillDescriptionText.Text =
            "Карточка создаётся автоматически для ключей, содержащих Skill и числовой ID.";
        EntryList.ItemsSource = null;
        UpdateEditor();
    }
}
