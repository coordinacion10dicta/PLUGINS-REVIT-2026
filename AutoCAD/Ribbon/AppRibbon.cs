using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Windows;

namespace AutoCAD.Ribbon
{
    public static class AppRibbon
    {
        private const string TabId = "DICTA_TAB";
        private const string TabTitle = "DICTA";
        private const string PanelTitle = "Seguridad Humana";

        private const string MenuButtonId = "DICTA_SGH_AREAS_MENU";
        private const string MenuButtonText = "Ocupaciones\nSGH";

        private const string ButtonCreateId = "DICTA_SGH_CREATE_AREA_BTN";
        private const string ButtonCreateText = "Crear Área";
        private const string CommandCreateString = "SGHAREAS ";

        private const string ButtonEditId = "DICTA_SGH_EDIT_AREA_BTN";
        private const string ButtonEditText = "Editar Área";
        private const string CommandEditString = "SGHEDITAREA ";

        private const string ButtonExcelId = "DICTA_SGH_EXCEL_BTN";
        private const string ButtonExcelText = "Exportar Excel";
        private const string CommandExcelString = "SGHEXPORTEXCEL ";

        public static void CreateRibbon()
        {
            if (ComponentManager.Ribbon != null)
            {
                BuildRibbon(ComponentManager.Ribbon);
            }
            else
            {
                ComponentManager.ItemInitialized += ComponentManager_ItemInitialized;
            }
        }

        private static void ComponentManager_ItemInitialized(object sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon != null)
            {
                ComponentManager.ItemInitialized -= ComponentManager_ItemInitialized;
                BuildRibbon(ComponentManager.Ribbon);
            }
        }

        private static void BuildRibbon(RibbonControl ribbon)
        {
            if (ribbon == null) return;

            // 1. Pestaña DICTA
            RibbonTab tab = ribbon.Tabs.FirstOrDefault(t => t.Id == TabId || t.Title == TabTitle);
            if (tab == null)
            {
                tab = new RibbonTab
                {
                    Title = TabTitle,
                    Id = TabId
                };
                ribbon.Tabs.Add(tab);
            }

            // Remover cualquier panel previo para evitar duplicados en la pestaña
            var existingPanels = tab.Panels.Where(p => p.Source != null && 
                (p.Source.Title == "SEGURIDAD HUMANA" || p.Source.Title == "SEGURID..." || p.Source.Title == "Seguridad Humana" || p.Source.Id == "DICTA_SGH_PANEL" || string.IsNullOrEmpty(p.Source.Title))).ToList();
            
            foreach (var p in existingPanels)
            {
                tab.Panels.Remove(p);
            }

            // 2. Panel "Seguridad Humana"
            RibbonPanelSource panelSource = new RibbonPanelSource
            {
                Title = PanelTitle,
                Id = "DICTA_SGH_PANEL"
            };

            RibbonPanel panel = new RibbonPanel
            {
                Source = panelSource,
                IsCollapsed = false,
                IsVisible = true,
                ResizeStyle = RibbonResizeStyles.NeverCollapsePanel
            };

            tab.Panels.Add(panel);
            panel.Source.Items.Clear();

            // Cargar icono principal
            BitmapImage imgIcon = LoadRibbonIcon("cotas.png");

            // 3. Botón único desplegable "Ocupaciones SGH"
            var menuButton = new RibbonSplitButton
            {
                Id = MenuButtonId,
                Text = MenuButtonText,
                ShowText = true,
                ShowImage = imgIcon != null,
                LargeImage = imgIcon,
                Image = imgIcon,
                Size = RibbonItemSize.Large,
                Orientation = Orientation.Vertical,
                IsSplit = false,
                IsSynchronizedWithCurrentItem = false,
                ListStyle = RibbonSplitButtonListStyle.List,
                CommandHandler = new RibbonCommandHandler()
            };

            // Opción 1: Crear Área
            var btnCreate = new RibbonButton
            {
                Id = ButtonCreateId,
                Text = ButtonCreateText,
                ShowText = true,
                ShowImage = false,
                Size = RibbonItemSize.Standard,
                CommandParameter = CommandCreateString,
                CommandHandler = new RibbonCommandHandler()
            };

            // Opción 2: Editar Área
            var btnEdit = new RibbonButton
            {
                Id = ButtonEditId,
                Text = ButtonEditText,
                ShowText = true,
                ShowImage = false,
                Size = RibbonItemSize.Standard,
                CommandParameter = CommandEditString,
                CommandHandler = new RibbonCommandHandler()
            };

            // Opción 3: Exportar a Excel
            var btnExcel = new RibbonButton
            {
                Id = ButtonExcelId,
                Text = ButtonExcelText,
                ShowText = true,
                ShowImage = false,
                Size = RibbonItemSize.Standard,
                CommandParameter = CommandExcelString,
                CommandHandler = new RibbonCommandHandler()
            };

            menuButton.Items.Add(btnCreate);
            menuButton.Items.Add(btnEdit);
            menuButton.Items.Add(btnExcel);

            panel.Source.Items.Add(menuButton);

            try
            {
                ribbon.UpdateLayout();
            }
            catch { }
        }

        private static BitmapImage LoadRibbonIcon(string imageName)
        {
            try
            {
                string assemblyDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(assemblyDir)) return null;

                string imagePath = System.IO.Path.Combine(assemblyDir, "Images", imageName);
                if (!System.IO.File.Exists(imagePath))
                {
                    imagePath = System.IO.Path.Combine(assemblyDir, "..", "Images", imageName);
                }

                if (System.IO.File.Exists(imagePath))
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    return bitmap;
                }
            }
            catch
            {
                // Manejo silencioso en caso de error de ruta o permisos
            }
            return null;
        }
    }

    public class RibbonCommandHandler : ICommand
    {
        public bool CanExecute(object parameter) => true;

        public event EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }

        public void Execute(object parameter)
        {
            string cmdToExecute = null;

            if (parameter is RibbonButton button && button.CommandParameter is string cmd)
            {
                cmdToExecute = cmd;
            }
            else if (parameter is RibbonSplitButton splitBtn && splitBtn.Current is RibbonButton currentBtn && currentBtn.CommandParameter is string splitCmd)
            {
                cmdToExecute = splitCmd;
            }
            else if (parameter is string cmdStr)
            {
                cmdToExecute = cmdStr;
            }

            if (!string.IsNullOrEmpty(cmdToExecute))
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    doc.SendStringToExecute(cmdToExecute, true, false, false);
                }
            }
        }
    }
}
