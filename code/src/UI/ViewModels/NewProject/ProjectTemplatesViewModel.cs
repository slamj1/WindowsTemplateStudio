﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.Templates.Core;
using Microsoft.Templates.Core.Diagnostics;
using Microsoft.Templates.Core.Gen;
using Microsoft.Templates.Core.Mvvm;
using Microsoft.Templates.UI.Resources;
using Microsoft.Templates.UI.Services;
using Microsoft.Templates.UI.ViewModels.Common;

namespace Microsoft.Templates.UI.ViewModels.NewProject
{
    public class ProjectTemplatesViewModel : Observable
    {
        public MetadataInfoViewModel ContextFramework { get; set; }
        public MetadataInfoViewModel ContextProjectType { get; set; }

        private string _pagesHeader;
        public string PagesHeader
        {
            get => _pagesHeader;
            set => SetProperty(ref _pagesHeader, value);
        }

        private string _featuresHeader;
        public string FeaturesHeader
        {
            get => _featuresHeader;
            set => SetProperty(ref _featuresHeader, value);
        }

        public string HomeName { get; set; }

        public ObservableCollection<ItemsGroupViewModel<TemplateInfoViewModel>> PagesGroups { get; } = new ObservableCollection<ItemsGroupViewModel<TemplateInfoViewModel>>();
        public ObservableCollection<ItemsGroupViewModel<TemplateInfoViewModel>> FeatureGroups { get; } = new ObservableCollection<ItemsGroupViewModel<TemplateInfoViewModel>>();

        public ObservableCollection<ObservableCollection<SavedTemplateViewModel>> SavedPages { get; } = new ObservableCollection<ObservableCollection<SavedTemplateViewModel>>();
        public ObservableCollection<SavedTemplateViewModel> SavedFeatures { get; } = new ObservableCollection<SavedTemplateViewModel>();

        private bool _hasSavedPages;
        public bool HasSavedPages
        {
            get => _hasSavedPages;
            private set => SetProperty(ref _hasSavedPages, value);
        }

        private bool _hasSavedFeatures;
        public bool HasSavedFeatures
        {
            get => _hasSavedFeatures;
            private set => SetProperty(ref _hasSavedFeatures, value);
        }

        private RelayCommand<SavedTemplateViewModel> _removeTemplateCommand;
        public RelayCommand<SavedTemplateViewModel> RemoveTemplateCommand => _removeTemplateCommand ?? (_removeTemplateCommand = new RelayCommand<SavedTemplateViewModel>(item => RemoveTemplate(item, true)));

        private RelayCommand<TemplateInfoViewModel> _addTemplateCommand;
        public RelayCommand<TemplateInfoViewModel> AddTemplateCommand => _addTemplateCommand ?? (_addTemplateCommand = new RelayCommand<TemplateInfoViewModel>(OnAddTemplateItem));

        private RelayCommand<TemplateInfoViewModel> _saveTemplateCommand;
        public RelayCommand<TemplateInfoViewModel> SaveTemplateCommand => _saveTemplateCommand ?? (_saveTemplateCommand = new RelayCommand<TemplateInfoViewModel>(OnSaveTemplateItem));

        private ICommand _openSummaryItemCommand;
        public ICommand OpenSummaryItemCommand => _openSummaryItemCommand ?? (_openSummaryItemCommand = new RelayCommand<SavedTemplateViewModel>(OnOpenSummaryItem));

        private ICommand _renameSummaryItemCommand;
        public ICommand RenameSummaryItemCommand => _renameSummaryItemCommand ?? (_renameSummaryItemCommand = new RelayCommand<SavedTemplateViewModel>(OnRenameSummaryItem));

        private ICommand _confirmRenameSummaryItemCommand;
        public ICommand ConfirmRenameSummaryItemCommand => _confirmRenameSummaryItemCommand ?? (_confirmRenameSummaryItemCommand = new RelayCommand<SavedTemplateViewModel>(OnConfirmRenameSummaryItem));

        public ProjectTemplatesViewModel()
        {
            SavedFeatures.CollectionChanged += (s, o) => { OnPropertyChanged(nameof(SavedFeatures)); };
            SavedPages.CollectionChanged += (s, o) => { OnPropertyChanged(nameof(SavedPages)); };
        }

        public IEnumerable<string> Names
        {
            get
            {
                var names = new List<string>();
                SavedPages.ToList().ForEach(spg => names.AddRange(spg.Select(sp => sp.ItemName)));
                names.AddRange(SavedFeatures.Select(sf => sf.ItemName));
                return names;
            }
        }

        public IEnumerable<string> Identities
        {
            get
            {
                var identities = new List<string>();
                SavedPages.ToList().ForEach(spg => identities.AddRange(spg.Select(sp => sp.Identity)));
                identities.AddRange(SavedFeatures.Select(sf => sf.Identity));
                return identities;
            }
        }

        public bool HasTemplatesAdded => SavedPages?.Count > 0 || SavedFeatures?.Count > 0;

        private void ValidateCurrentTemplateName(SavedTemplateViewModel item)
        {
            if (item.NewItemName != item.ItemName)
            {
                var validators = new List<Validator>()
                {
                    new ExistingNamesValidator(Names),
                    new ReservedNamesValidator()
                };
                if (item.CanChooseItemName)
                {
                    validators.Add(new DefaultNamesValidator());
                }
                var validationResult = Naming.Validate(item.NewItemName, validators);

                item.IsValidName = validationResult.IsValid;
                item.ErrorMessage = string.Empty;

                if (!item.IsValidName)
                {
                    item.ErrorMessage = StringRes.ResourceManager.GetString($"ValidationError_{validationResult.ErrorType}");

                    if (string.IsNullOrWhiteSpace(item.ErrorMessage))
                    {
                        item.ErrorMessage = StringRes.UndefinedErrorString;
                    }
                    MainViewModel.Current.SetValidationErrors(item.ErrorMessage);
                    throw new Exception(item.ErrorMessage);
                }
            }
            MainViewModel.Current.CleanStatus(true);
        }

        private void ValidateNewTemplateName(TemplateInfoViewModel template)
        {
            var validators = new List<Validator>()
            {
                new ExistingNamesValidator(Names),
                new ReservedNamesValidator()
            };
            if (template.CanChooseItemName)
            {
                validators.Add(new DefaultNamesValidator());
            }
            var validationResult = Naming.Validate(template.NewTemplateName, validators);

            template.IsValidName = validationResult.IsValid;
            template.ErrorMessage = string.Empty;

            if (!template.IsValidName)
            {
                template.ErrorMessage = StringRes.ResourceManager.GetString($"ValidationError_{validationResult.ErrorType}");

                if (string.IsNullOrWhiteSpace(template.ErrorMessage))
                {
                    template.ErrorMessage = StringRes.UndefinedErrorString;
                }
                MainViewModel.Current.SetValidationErrors(template.ErrorMessage);
                throw new Exception(template.ErrorMessage);
            }
            MainViewModel.Current.CleanStatus(true);
        }

        public async Task InitializeAsync()
        {
            MainViewModel.Current.Title = StringRes.ProjectTemplatesTitle;
            ContextProjectType = MainViewModel.Current.ProjectSetup.SelectedProjectType;
            ContextFramework = MainViewModel.Current.ProjectSetup.SelectedFramework;

            if (PagesGroups.Count == 0)
            {
                var pages = GenContext.ToolBox.Repo.Get(t => t.GetTemplateType() == TemplateType.Page && t.GetFrameworkList().Contains(ContextFramework.Name))
                                                   .Select(t => new TemplateInfoViewModel(t, GenComposer.GetAllDependencies(t, ContextFramework.Name), AddTemplateCommand, SaveTemplateCommand, ValidateNewTemplateName));

                var groups = pages.GroupBy(t => t.Group).Select(gr => new ItemsGroupViewModel<TemplateInfoViewModel>(gr.Key as string, gr.ToList().OrderBy(t => t.Order))).OrderBy(gr => gr.Title);

                PagesGroups.AddRange(groups);
                PagesHeader = string.Format(StringRes.GroupPagesHeader_SF, pages.Count());
            }

            if (FeatureGroups.Count == 0)
            {
                var features = GenContext.ToolBox.Repo.Get(t => t.GetTemplateType() == TemplateType.Feature && t.GetFrameworkList().Contains(ContextFramework.Name) && !t.GetIsHidden())
                                                      .Select(t => new TemplateInfoViewModel(t, GenComposer.GetAllDependencies(t, ContextFramework.Name), AddTemplateCommand, SaveTemplateCommand, ValidateNewTemplateName));

                var groups = features.GroupBy(t => t.Group).Select(gr => new ItemsGroupViewModel<TemplateInfoViewModel>(gr.Key as string, gr.ToList().OrderBy(t => t.Order))).OrderBy(gr => gr.Title);

                FeatureGroups.AddRange(groups);
                FeaturesHeader = string.Format(StringRes.GroupFeaturesHeader_SF, features.Count());
            }

            if (SavedPages.Count == 0 && SavedFeatures.Count == 0)
            {
                SetupTemplatesFromLayout(ContextProjectType.Name, ContextFramework.Name);
                MainViewModel.Current.RebuildLicenses();
            }
            MainViewModel.Current.UpdateCanFinish(true);
            CloseTemplatesEdition();
            await Task.CompletedTask;
        }

        public void ResetSelection()
        {
            SavedPages.Clear();
            SavedFeatures.Clear();
            PagesGroups.Clear();
            FeatureGroups.Clear();
        }

        private void OnAddTemplateItem(TemplateInfoViewModel template)
        {
            if (template.CanChooseItemName)
            {
                var validators = new List<Validator>()
                {
                    new ReservedNamesValidator(),
                    new ExistingNamesValidator(Names),
                    new DefaultNamesValidator()
                };
                template.NewTemplateName = Naming.Infer(template.Template.GetDefaultName(), validators);
                CloseTemplatesEdition();
                template.IsEditionEnabled = true;
            }
            else
            {
                var validators = new List<Validator>()
                {
                    new ReservedNamesValidator(),
                    new ExistingNamesValidator(Names)
                };
                template.NewTemplateName = Naming.Infer(template.Template.GetDefaultName(), validators);
                SetupTemplateAndDependencies((template.NewTemplateName, template.Template));
                var isAlreadyDefined = IsTemplateAlreadyDefined(template.Template.Identity);
                template.UpdateTemplateAvailability(isAlreadyDefined);
            }
        }

        private void OnSaveTemplateItem(TemplateInfoViewModel template)
        {
            if (template.IsValidName)
            {
                SetupTemplateAndDependencies((template.NewTemplateName, template.Template));
                template.CloseEdition();

                var isAlreadyDefined = IsTemplateAlreadyDefined(template.Template.Identity);
                template.UpdateTemplateAvailability(isAlreadyDefined);
            }
        }

        private void OnRenameSummaryItem(SavedTemplateViewModel item)
        {
            CloseSummaryItemsEdition();
            item.IsEditionEnabled = true;
            item.TryClose();
        }

        private void OnConfirmRenameSummaryItem(SavedTemplateViewModel item)
        {
            if (item.NewItemName == item.ItemName)
            {
                item.IsEditionEnabled = false;
                return;
            }
            var validators = new List<Validator>()
            {
                new ExistingNamesValidator(Names),
                new ReservedNamesValidator()
            };
            if (item.CanChooseItemName)
            {
                validators.Add(new DefaultNamesValidator());
            }
            var validationResult = Naming.Validate(item.NewItemName, validators);

            if (validationResult.IsValid)
            {
                item.ItemName = item.NewItemName;

                if (item.IsHome)
                {
                    HomeName = item.ItemName;
                }

                AppHealth.Current.Telemetry.TrackEditSummaryItemAsync(EditItemActionEnum.Rename).FireAndForget();
            }
            else
            {
                item.NewItemName = item.ItemName;
            }
            item.IsEditionEnabled = false;
            MainViewModel.Current.CleanStatus(true);
        }

        private void OnOpenSummaryItem(SavedTemplateViewModel item)
        {
            if (!item.IsOpen)
            {
                SavedPages.ToList().ForEach(pg => pg.ToList().ForEach(p => TryClose(p, item)));
                SavedFeatures.ToList().ForEach(f => TryClose(f, item));
                item.IsOpen = true;
            }
            else
            {
                item.TryClose();
            }
        }

        private void TryClose(SavedTemplateViewModel target, SavedTemplateViewModel origin)
        {
            if (target.IsOpen && target.ItemName != origin.ItemName)
            {
                target.TryClose();
            }
        }

        public void SetHomePage(SavedTemplateViewModel item)
        {
            if (!item.IsHome)
            {
                foreach (var spg in SavedPages)
                {
                    spg.ToList().ForEach(sp => sp.TryReleaseHome());
                }

                item.IsHome = true;
                HomeName = item.ItemName;
                AppHealth.Current.Telemetry.TrackEditSummaryItemAsync(EditItemActionEnum.SetHome).FireAndForget();
            }
        }

        private void RemoveTemplate(SavedTemplateViewModel item, bool showErrors)
        {
            // Look if is there any templates that depends on item
            if (AnySavedTemplateDependsOnItem(item, showErrors))
            {
                return;
            }

            // Remove template
            if (SavedPages[item.GenGroup].Contains(item))
            {
                SavedPages[item.GenGroup].Remove(item);
                HasSavedPages = SavedPages.Any(g => g.Any());
            }
            else if (SavedFeatures.Contains(item))
            {
                SavedFeatures.Remove(item);
                HasSavedFeatures = SavedFeatures.Any();
            }

            TryRemoveHiddenDependencies(item);

            MainViewModel.Current.FinishCommand.OnCanExecuteChanged();
            UpdateTemplatesAvailability();
            MainViewModel.Current.RebuildLicenses();

            AppHealth.Current.Telemetry.TrackEditSummaryItemAsync(EditItemActionEnum.Remove).FireAndForget();
        }

        private bool AnySavedTemplateDependsOnItem(SavedTemplateViewModel item, bool showErrors)
        {
            SavedTemplateViewModel dependencyItem = null;
            foreach (var group in SavedPages)
            {
                dependencyItem = group.FirstOrDefault(st => st.DependencyList.Any(d => d == item.Identity));
                if (dependencyItem != null)
                {
                    break;
                }
            }

            if (dependencyItem == null)
            {
                dependencyItem = SavedFeatures.FirstOrDefault(st => st.DependencyList.Any(d => d == item.Identity));
            }

            if (dependencyItem != null)
            {
                if (showErrors)
                {
                    string message = string.Format(StringRes.ValidationError_CanNotRemoveTemplate_SF, item.TemplateName, dependencyItem.TemplateName, dependencyItem.TemplateType);
                    MainViewModel.Current.SetStatus(StatusViewModel.Warning(message, false, 5));
                }
                return true;
            }

            return false;
        }

        private void TryRemoveHiddenDependencies(SavedTemplateViewModel item)
        {
            foreach (var identity in item.DependencyList)
            {
                var dependency = SavedFeatures.FirstOrDefault(sf => sf.Identity == identity);
                if (dependency == null)
                {
                    foreach (var pageGroup in SavedPages)
                    {
                        dependency = pageGroup.FirstOrDefault(sf => sf.Identity == identity);
                        if (dependency != null)
                        {
                            break;
                        }
                    }
                }

                if (dependency != null)
                {
                    // If the template is not hidden we can not remove it because it could be added in wizard
                    if (dependency.IsHidden)
                    {
                        // Look if there are another saved template that depends on it.
                        // For example, if it's added two different chart pages, when remove the first one SampleDataService can not be removed, but if no saved templates use SampleDataService, it can be removed.
                        if (!SavedFeatures.Any(sf => sf.DependencyList.Any(d => d == dependency.Identity)) || SavedPages.Any(spg => spg.Any(sp => sp.DependencyList.Any(d => d == dependency.Identity))))
                        {
                            RemoveTemplate(dependency, false);
                        }
                    }
                }
            }
        }

        private bool IsTemplateAlreadyDefined(string identity) => Identities.Any(i => i == identity);

        public bool CloseTemplatesEdition()
        {
            bool isEditingTemplate = false;
            PagesGroups.ToList().ForEach(g => g.Templates.ToList().ForEach(t =>
            {
                if (t.CloseEdition())
                {
                    isEditingTemplate = true;
                }
            }));
            FeatureGroups.ToList().ForEach(g => g.Templates.ToList().ForEach(t =>
            {
                if (t.CloseEdition())
                {
                    isEditingTemplate = true;
                }
            }));
            return isEditingTemplate;
        }

        public void CloseSummaryItemsEdition()
        {
            SavedPages.ToList().ForEach(spg => spg.ToList().ForEach(p => p.OnCancelRename()));
            SavedFeatures.ToList().ForEach(f => f.OnCancelRename());
        }

        public void DropTemplate(object sender, DragAndDropEventArgs<SavedTemplateViewModel> e)
        {
            if (SavedPages.Count > 0 && SavedPages.Count >= e.ItemData.GenGroup + 1)
            {
                var items = SavedPages[e.ItemData.GenGroup];
                if (items.Count > 1)
                {
                    if (e.NewIndex == 0)
                    {
                        SetHomePage(e.ItemData);
                    }
                    if (e.OldIndex > -1)
                    {
                        SavedPages[e.ItemData.GenGroup].Move(e.OldIndex, e.NewIndex);
                    }
                    SetHomePage(items.First());
                }
            }
        }

        private void UpdateTemplatesAvailability()
        {
            PagesGroups.ToList().ForEach(g => g.Templates.ToList().ForEach(t =>
            {
                var isAlreadyDefined = IsTemplateAlreadyDefined(t.Template.Identity);
                t.UpdateTemplateAvailability(isAlreadyDefined);
            }));

            FeatureGroups.ToList().ForEach(g => g.Templates.ToList().ForEach(t =>
            {
                var isAlreadyDefined = IsTemplateAlreadyDefined(t.Template.Identity);
                t.UpdateTemplateAvailability(isAlreadyDefined);
            }));
        }

        private void UpdateSummaryTemplates()
        {
            foreach (var spg in SavedPages)
            {
                spg.ToList().ForEach(sp => sp.UpdateAllowDragAndDrop(SavedPages[0].Count));
            }
        }

        private void SetupTemplatesFromLayout(string projectTypeName, string frameworkName)
        {
            var layout = GenComposer.GetLayoutTemplates(projectTypeName, frameworkName);

            foreach (var item in layout)
            {
                if (item.Template != null)
                {
                    SetupTemplateAndDependencies((item.Layout.name, item.Template), !item.Layout.@readonly);
                }
            }
        }

        private void SetupTemplateAndDependencies((string name, ITemplateInfo template) item, bool isRemoveEnabled = true)
        {
            SaveNewTemplate(item, isRemoveEnabled);
            var dependencies = GenComposer.GetAllDependencies(item.template, ContextFramework.Name);

            foreach (var dependencyTemplate in dependencies)
            {
                if (!Identities.Any(i => i == dependencyTemplate.Identity))
                {
                    SaveNewTemplate((dependencyTemplate.GetDefaultName(), dependencyTemplate), isRemoveEnabled);
                }
            }

            MainViewModel.Current.RebuildLicenses();
        }

        private void SaveNewTemplate((string name, ITemplateInfo template) item, bool isRemoveEnabled = true)
        {
            var newItem = new SavedTemplateViewModel(item, isRemoveEnabled, OpenSummaryItemCommand, RemoveTemplateCommand, RenameSummaryItemCommand, ConfirmRenameSummaryItemCommand, ValidateCurrentTemplateName);

            if (item.template.GetTemplateType() == TemplateType.Page)
            {
                if (SavedPages.Count == 0)
                {
                    HomeName = item.name;
                    newItem.IsHome = true;
                }
                while (SavedPages.Count < newItem.GenGroup + 1)
                {
                    var items = new ObservableCollection<SavedTemplateViewModel>();
                    SavedPages.Add(items);
                    MainViewModel.Current.DefineDragAndDrop(items, SavedPages.Count == 1);
                }
                SavedPages[newItem.GenGroup].Add(newItem);
                HasSavedPages = true;
            }
            else if (item.template.GetTemplateType() == TemplateType.Feature)
            {
                SavedFeatures.Add(newItem);
                HasSavedFeatures = true;
            }
            UpdateTemplatesAvailability();
            UpdateSummaryTemplates();
        }
    }
}
