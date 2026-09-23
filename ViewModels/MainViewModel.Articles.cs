using System.Collections.ObjectModel;
using System.Windows.Input;
using Tally.Models;
using Tally.Services;

namespace Tally.ViewModels;

/// <summary>Edit the article list and categories (the "Articles &amp; Categories" window).</summary>
public sealed partial class MainViewModel
{
    private static readonly StringComparer NameComparer = StringComparer.Create(Fmt.De, ignoreCase: true);

    private string _articleSearch = "";
    private string _newCategoryName = "";
    private string _newArticleName = "";
    private string _newArticleCategory = "";

    public ObservableCollection<Article> Articles => _data.Articles!;
    public ObservableCollection<Category> Categories => _data.Categories!;

    /// <summary>Articles matching the search, sorted alphabetically.</summary>
    public ObservableCollection<Article> FilteredArticles { get; } = new();

    public string ArticleSearch
    {
        get => _articleSearch;
        set { if (Set(ref _articleSearch, value ?? "")) RefreshArticleList(); }
    }

    public string ArticleCountText => string.Format(LocalizationManager.T("S_ArticleCountText"), FilteredArticles.Count, Articles.Count);

    public string NewCategoryName { get => _newCategoryName; set => Set(ref _newCategoryName, value ?? ""); }
    public string NewArticleName { get => _newArticleName; set => Set(ref _newArticleName, value ?? ""); }

    public string NewArticleCategory
    {
        get => _newArticleCategory;
        set { if (value != null) Set(ref _newArticleCategory, value); }
    }

    /// <summary>
    /// Rebuilds the filtered list. Deliberately NOT called on every name change,
    /// so a row doesn't jump away while you're still editing it.
    /// </summary>
    private void RefreshArticleList()
    {
        var q = _articleSearch.Trim();
        IEnumerable<Article> source = Articles;
        if (q.Length > 0)
            source = source.Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

        CollectionSync.Sync(FilteredArticles, source.OrderBy(a => a.Name, NameComparer).ToList());
        OnPropertyChanged(nameof(ArticleCountText));
    }

    private void EnsureValidNewArticleCategory()
    {
        if (!Categories.Any(c => c.Name == _newArticleCategory))
            NewArticleCategory = Categories.FirstOrDefault()?.Name ?? "";
    }

    // ---- categories ----

    private ICommand? _addCategory;
    public ICommand AddCategoryCommand => _addCategory ??= new RelayCommand(_ => AddCategory());

    private void AddCategory()
    {
        var name = NewCategoryName.Trim();
        if (name.Length == 0) { RequestFocus("NewCategoryName"); return; }
        if (Categories.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            RequestFocus("NewCategoryName"); // already exists
            return;
        }

        Categories.Add(new Category { Name = name });
        NewCategoryName = "";
    }

    private ICommand? _deleteCategory;
    public ICommand DeleteCategoryCommand => _deleteCategory ??= new RelayCommand(c => DeleteCategory(c as Category));

    private void DeleteCategory(Category? category)
    {
        if (category == null) return;

        var articleCount = Articles.Count(a => string.Equals(a.Category, category.Name, StringComparison.OrdinalIgnoreCase));
        var itemCount = Parties.Sum(p => p.Items.Count(i => string.Equals(i.Category, category.Name, StringComparison.OrdinalIgnoreCase)));

        if (articleCount + itemCount > 0)
        {
            var ok = _ui.Confirm(LocalizationManager.T("S_DeleteCategoryTitle"),
                string.Format(LocalizationManager.T("S_DeleteCategoryMsg"), category.Name, articleCount, itemCount),
                LocalizationManager.T("S_Delete"), LocalizationManager.T("S_Cancel"), danger: true);
            if (!ok) return;
        }

        Categories.Remove(category);
    }

    // ---- articles ----

    private ICommand? _addArticle;
    public ICommand AddArticleCommand => _addArticle ??= new RelayCommand(_ => AddArticle());

    private void AddArticle()
    {
        var name = NewArticleName.Trim();
        if (name.Length == 0) { RequestFocus("NewArticleName"); return; }

        Articles.Add(new Article { Name = name, Category = NewArticleCategory });
        NewArticleName = "";
        RequestFocus("NewArticleName");
    }

    private ICommand? _deleteArticle;
    public ICommand DeleteArticleCommand => _deleteArticle ??= new RelayCommand(a =>
    {
        if (a is Article article) Articles.Remove(article);
    });

    private ICommand? _addDefaultArticles;
    public ICommand AddDefaultArticlesCommand => _addDefaultArticles ??= new RelayCommand(_ => AddDefaultArticles());

    /// <summary>Adds default articles (and their categories) that are still missing. Existing ones stay untouched.</summary>
    private void AddDefaultArticles()
    {
        var known = new HashSet<string>(Articles.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
        var toAdd = DefaultArticles.All.Where(a => known.Add(a.Name)).ToList();

        if (toAdd.Count == 0)
        {
            _ui.Info(LocalizationManager.T("S_DefaultArticlesTitle"), LocalizationManager.T("S_DefaultArticlesAllPresent"));
            return;
        }

        foreach (var name in toAdd.Select(a => a.Category).Distinct())
            if (!Categories.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                Categories.Add(new Category { Name = name });

        foreach (var a in toAdd) Articles.Add(new Article { Name = a.Name, Category = a.Category });

        _ui.Info(LocalizationManager.T("S_DefaultArticlesTitle"), string.Format(LocalizationManager.T("S_DefaultArticlesAdded"), toAdd.Count));
    }

    private ICommand? _openArticles;
    public ICommand OpenArticlesCommand => _openArticles ??= new RelayCommand(_ =>
    {
        ArticleSearch = "";
        _ui.OpenArticleEditor();
    });
}
