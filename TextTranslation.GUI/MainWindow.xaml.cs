using DocumentTranslationService.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;

namespace TextTranslation.GUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        internal readonly ViewModel ViewModel;
        private PerLanguageData perLanguageData = new();

        #region Global

        public MainWindow()
        {
            InitializeComponent();
            ViewModel viewModel = new();
            DataContext = viewModel;
            viewModel.OnLanguagesUpdate += ViewModel_OnLanguagesUpdate;
            viewModel.OnKeyVaultAuthenticationStart += ViewModel_OnKeyVaultAuthenticationStart;
            viewModel.OnKeyVaultAuthenticationComplete += ViewModel_OnKeyVaultAuthenticationComplete;
            AppSettingsSetter.SettingsReadComplete += AppSettingsSetter_SettingsReadComplete;
            ViewModel = viewModel;
            fromLanguageBox.ItemsSource = ViewModel.FromLanguageList;

            // Removed UI support for Category text box
            //CategoryTextBox.ItemsSource = ViewModel.categories.MyCategoryList;
        }

        private void ViewModel_OnKeyVaultAuthenticationStart(object sender, EventArgs e)
        {
            StatusBarSText1.Text = Properties.Resources.msg_SigningIn;
        }

        private void ViewModel_OnKeyVaultAuthenticationComplete(object sender, EventArgs e)
        {
            if (ViewModel.Settings.UsingKeyVault) TranslateTextTab.IsSelected = true;
            StatusBarSText1.Text = Properties.Resources.msg_SignInComplete;
        }

        private void ViewModel_OnLanguagesUpdate(object sender, EventArgs e)
        {
            if (ViewModel.UISettings.lastFromLanguage is not null)
                fromLanguageBox.SelectedValue = ViewModel.UISettings.lastFromLanguage;
            //else fromLanguageBox.SelectedIndex = 0;
            else fromLanguageBox.SelectedValue = ViewModel.FromLanguageList.Where(x => x.LangCode == "en-us");
        }

        private void AppSettingsSetter_SettingsReadComplete(object sender, EventArgs e)
        {
            EnableTabs();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ViewModel.OnLanguagesFailed += ViewModel_OnLanguagesFailed;
            if (ViewModel.localSettings.UsingKeyVault) SettingsTab.IsSelected = true;
            try
            {
                await ViewModel.InitializeAsync();
            }
            catch (ArgumentNullException ex)
            {
                SettingsTab.IsSelected = true;
                if (ex.ParamName is "SubscriptionKey" or null) TranslateTextTab.IsEnabled = false;
            }
            catch (KeyVaultAccessException ex)
            {
                SettingsTab.IsSelected = true;
                TranslateTextTab.IsEnabled = false;
                System.Resources.ResourceManager resx = new("DocumentTranslation.GUI.Properties.Resources", System.Reflection.Assembly.GetExecutingAssembly());
                StatusBarSText2.Text = ex.Message;
            }
            catch (System.AggregateException ex)
            {
                SettingsTab.IsSelected = true;
                TranslateTextTab.IsEnabled = false;
                StatusBarSText1.Text = Properties.Resources.msg_TestFailed;
                StatusBarSText2.Text = ex.InnerException.Message;
            }
            catch (DocumentTranslationService.Core.DocumentTranslationService.CredentialsException ex)
            {
                StatusBarTText1.Text = Properties.Resources.msg_TestFailed;
                StatusBarTText2.Text = ex.Message;
            }

            // Removed UI Suport for Category
            //CategoryTextBox.SelectedValue = ViewModel.UISettings.lastCategoryText;
            ViewModel_OnLanguagesUpdate(this, EventArgs.Empty);
        }

        private void ViewModel_OnLanguagesFailed(object sender, string e)
        {
            StatusBarTText1.Text = Properties.Resources.msg_Error;
            StatusBarTText2.Text = e;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (fromLanguageBox.SelectedIndex >= 0) ViewModel.UISettings.lastFromLanguage = fromLanguageBox.SelectedValue as string;

            // Removed UI Suport for Category
            //if (CategoryTextBox.SelectedItem is not null) ViewModel.UISettings.lastCategoryText = ((MyCategory)CategoryTextBox.SelectedItem).Name ?? string.Empty;
            //else ViewModel.UISettings.lastCategoryText = null;
            ViewModel.SaveUISettings();
        }


        private async void EnableTabs()
        {
            await Task.Delay(10);
            TranslateTextTab.IsEnabled = true;
            if (string.IsNullOrEmpty(ViewModel.Settings.SubscriptionKey)) { TranslateTextTab.IsEnabled = false; return; }
            if (string.IsNullOrEmpty(ViewModel.Settings.AzureRegion)) TranslateTextTab.IsEnabled = false;
            if (ViewModel.localSettings.UsingKeyVault)
            {
                keyVaultName.Text = ViewModel.localSettings.AzureKeyVaultName;
            }
            else
            {
                subscriptionKey.Password = ViewModel.localSettings.SubscriptionKey;
                region.Text = ViewModel.localSettings.AzureRegion;
                storageConnectionString.Text = ViewModel.localSettings.ConnectionStrings.StorageConnectionString;
                resourceName.Text = ViewModel.localSettings.AzureResourceName;
                textTransEndpoint.Text = ViewModel.localSettings.TextTransEndpoint;
            }
        }

        #endregion Global
      
        #region TextTranslation
        private async void TranslateButton_Click(object sender, RoutedEventArgs e)
        {
            // Removed UI Suport for Category
            //ViewModel.documentTranslationService.Category = CategoryTextBox.SelectedItem is not null ? ((MyCategory)CategoryTextBox.SelectedItem).ID : null;
            try
            {
                outputBox.Text = await ViewModel.TranslateTextAsync(inputBox.Text, fromLanguageBox.SelectedValue as string, ViewModel.ToLanguage.LangCode);
                StatusBarTText2.Text = $"{inputBox.Text.Length} {Properties.Resources.msg_TranslateButton_Click_CharactersTranslated}";
                await Task.Delay(2000);
                StatusBarTText2.Text = string.Empty;
            }
            catch (InvalidCategoryException)
            {
                outputBox.Text = string.Empty;
                StatusBarTText1.Text = Properties.Resources.msg_TranslateButton_Click_Error;
                StatusBarTText2.Text = Properties.Resources.msg_TranslateButton_Click_InvalidCategory;
                await Task.Delay(2000);
                StatusBarTText1.Text = string.Empty;
                StatusBarTText2.Text = string.Empty;
            }
            catch (AccessViolationException ex)
            {
                outputBox.Text = string.Empty;
                StatusBarTText1.Text = Properties.Resources.msg_TranslateButton_Click_Error;
                StatusBarTText2.Text = ex.Message;
                await Task.Delay(2000);
                StatusBarTText1.Text = string.Empty;
                StatusBarTText2.Text = string.Empty;
            }
        }

        private void FromLanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (fromLanguageBox.SelectedItem is not Language lang) return;
            if (lang.Bidi)
            {
                inputBox.TextAlignment = TextAlignment.Right;
                inputBox.FlowDirection = System.Windows.FlowDirection.RightToLeft;
                inputBox.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Right;
            }
            else
            {
                inputBox.TextAlignment = TextAlignment.Left;
                inputBox.FlowDirection = System.Windows.FlowDirection.LeftToRight;
                inputBox.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
            }
        }

        private void TranslateTextTab_Loaded(object sender, RoutedEventArgs e)
        {

        }

        #endregion TextTranslation

        #region Settings
        private void TabItemAuthentication_Loaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel.localSettings.UsingKeyVault)
            {
                subscriptionKey.IsEnabled = false;
                region.IsEnabled = false;
                storageConnectionString.IsEnabled = false;
                resourceName.IsEnabled = false;
            }
            else
            {
                subscriptionKey.IsEnabled = true;
                region.IsEnabled = true;
                storageConnectionString.IsEnabled = true;
                resourceName.IsEnabled = true;
            }
            keyVaultName.Text = ViewModel.localSettings.AzureKeyVaultName;
            subscriptionKey.Password = ViewModel.localSettings.SubscriptionKey;
            region.Text = ViewModel.localSettings.AzureRegion;
            storageConnectionString.Text = ViewModel.localSettings.ConnectionStrings?.StorageConnectionString;
            resourceName.Text = ViewModel.localSettings.AzureResourceName;
            textTransEndpoint.Text = ViewModel.localSettings.TextTransEndpoint;
            experimentalCheckbox.IsChecked = ViewModel.localSettings.ShowExperimental;
            flightString.Text = ViewModel.localSettings.FlightString;
        }

        private void SubscriptionKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            ViewModel.localSettings.SubscriptionKey = subscriptionKey.Password;
        }

        private void Region_TextChanged(object sender, TextChangedEventArgs e)
        {
            ViewModel.localSettings.AzureRegion = region.Text;
            if (ViewModel.documentTranslationService is not null) ViewModel.documentTranslationService.AzureRegion = region.Text;
        }

        private void ResourceName_TextChanged(object sender, TextChangedEventArgs e)
        {
            ViewModel.localSettings.AzureResourceName = resourceName.Text;
        }

        private void StorageConnection_TextChanged(object sender, TextChangedEventArgs e)
        {
            ViewModel.localSettings.ConnectionStrings ??= new Connectionstrings();
            ViewModel.localSettings.ConnectionStrings.StorageConnectionString = storageConnectionString.Text;
        }

        private async void TestSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            StatusBarSText2.Text = string.Empty;
            StatusBarSText1.Text = Properties.Resources.Label_Testing;
            try
            {
                await ViewModel.InitializeAsync(true);
                await ViewModel.documentTranslationService.TryCredentials();
                StatusBarSText1.Text = Properties.Resources.msg_TestPassed;
            }
            catch (DocumentTranslationService.Core.DocumentTranslationService.CredentialsException ex)
            {
                string message;
                if (ex.Message == "name") message = Properties.Resources.msg_ResourceNameIncorrect;
                else message = ex.Message;
                StatusBarSText1.Text = Properties.Resources.msg_TestFailed;
                StatusBarSText2.Text = message;
            }
            catch (ArgumentNullException ex)
            {
                StatusBarSText1.Text = Properties.Resources.msg_TestFailed;
                StatusBarSText2.Text = ex.Message;
            }
            catch (KeyVaultAccessException ex)
            {
                StatusBarSText1.Text = Properties.Resources.msg_TestFailed;
                StatusBarSText2.Text = ex.Message;
            }
            catch (System.AggregateException ex)
            {
                StatusBarSText1.Text = Properties.Resources.msg_TestFailed;
                StatusBarSText2.Text = ex.InnerException.Message;
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                StatusBarSText1.Text = Properties.Resources.msg_TestFailed;
                StatusBarSText2.Text = ex.InnerException.Message;
            }
            await Task.Delay(10000);
            StatusBarSText1.Text = string.Empty;
            StatusBarSText2.Text = string.Empty;
        }

        private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            StatusBarSText2.Text = string.Empty;
            StatusBarSText1.Text = Properties.Resources.msg_SettingsSaved;
            ViewModel.SaveAppSettings();
            if (ViewModel.localSettings.UsingKeyVault) StatusBarSText1.Text = Properties.Resources.msg_SigningIn;
            try
            {
                await ViewModel.InitializeAsync();
            }
            catch
            {
                TestSettingsButton_Click(this, null);
            }
            if (ViewModel.localSettings.UsingKeyVault) StatusBarSText1.Text = Properties.Resources.msg_SignInComplete;
            EnableTabs();
            await Task.Delay(3000);
            StatusBarSText1.Text = string.Empty;
        }

        // Removed UI Suport for Category
        //private void CategoriesTab_Loaded(object sender, RoutedEventArgs e)
        //{
        //    CategoriesGridView.AllowUserToAddRows = true;
        //    CategoriesGridView.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        //    CategoriesGridView.DataSource = ViewModel.categories.MyCategoryList;
        //    CategoriesGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        //    CategoriesGridView.Columns[0].FillWeight = 2;
        //    CategoriesGridView.Columns[0].HeaderText = Properties.Resources.label_CategoryName;
        //    CategoriesGridView.Columns[1].FillWeight = 3;
        //    CategoriesGridView.Columns[1].HeaderText = Properties.Resources.label_CategoryId;
        //}

        // Removed UI Suport for Category
        //private void AddCategoryButton_Click(object sender, RoutedEventArgs e)
        //{
        //    ViewModel.AddCategory(CategoriesGridView.SelectedCells);
        //}

        // Removed UI Suport for Category
        //private void DeleteCategoryButton_Click(object sender, RoutedEventArgs e)
        //{
        //    ViewModel.DeleteCategory(CategoriesGridView.SelectedCells);
        //}

        // Removed UI Suport for Category
        //private async void SaveCategoriesButton_Click(object sender, RoutedEventArgs e)
        //{
        //    SavedCategoriesText.Visibility = Visibility.Visible;
        //    CategoriesGridView.EndEdit();
        //    ViewModel.SaveCategories();
        //    await Task.Delay(500);
        //    SavedCategoriesText.Visibility = Visibility.Hidden;
        //}

        // Removed UI Suport for Category
        //private void CategoryTextClearButton_Click(object sender, RoutedEventArgs e)
        //{
        //    CategoryTextBox.SelectedItem = null;
        //    CategoryTextClearButton.Visibility = Visibility.Hidden;
        //}

        // Removed UI Suport for Category
        //private void CategoryTextBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        //{
        //    if (CategoryTextBox.SelectedValue is not null) CategoryTextClearButton.Visibility = Visibility.Visible;
        //    else CategoryTextClearButton.Visibility = Visibility.Hidden;
        //}

        private void ExperimentalCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            ViewModel.localSettings.ShowExperimental = experimentalCheckbox.IsChecked.Value;
        }

        private void ExperimentalCheckbox_Unchecked(object sender, RoutedEventArgs e)
        {
            ViewModel.localSettings.ShowExperimental = experimentalCheckbox.IsChecked.Value;
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }

        private void KeyVaultName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(keyVaultName.Text))
            {
                subscriptionKey.IsEnabled = true;
                region.IsEnabled = true;
                resourceName.IsEnabled = true;
                storageConnectionString.IsEnabled = true;
                textTransEndpoint.IsEnabled = true;
            }
            else
            {
                subscriptionKey.IsEnabled = false;
                region.IsEnabled = false;
                resourceName.IsEnabled = false;
                storageConnectionString.IsEnabled = false;
                textTransEndpoint.IsEnabled = false;
            }
            ViewModel.localSettings.AzureKeyVaultName = keyVaultName.Text;
        }

        private void KeyVaultNameClearButton_Click(object sender, RoutedEventArgs e)
        {
            keyVaultName.Text = string.Empty;
            ViewModel.localSettings.AzureKeyVaultName = string.Empty;
            StatusBarSText1.Text = string.Empty;
            StatusBarSText2.Text = string.Empty;
        }

        private void TextTransEndpoint_TextChanged(object sender, TextChangedEventArgs e)
        {
            ViewModel.localSettings.TextTransEndpoint = textTransEndpoint.Text;
        }

        private void FlightString_TextChanged(object sender, TextChangedEventArgs e)
        {
            ViewModel.localSettings.FlightString = flightString.Text;
        }
        #endregion Settings

        private void TabLanguages_Loaded(object sender, RoutedEventArgs e)
        {
            LanguagesDataGrid.AutoGenerateColumns = true;
            LanguagesDataGrid.ItemsSource = ViewModel.ToLanguageList;
        }
    }
}
