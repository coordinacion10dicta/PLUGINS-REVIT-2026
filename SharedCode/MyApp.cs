using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace MiNamespace
{
    public class MyApp : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // 1. Nombre de la pestaña que quieres (ya existente o se creará)
                string tabName = "DICTA";

                // 2. Intentar crear la pestaña "DICTA" (si ya existe, se ignora el error)
                try
                {
                    application.CreateRibbonTab(tabName);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // La pestaña ya existe, no pasa nada
                }

                // 3. Crear o recuperar un panel llamado "Mi Panel"
                RibbonPanel panel;
                try
                {
                    panel = application.CreateRibbonPanel(tabName, "Mi Panel");
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // El panel ya existe: el plugin ya fue cargado en esta sesión
                    return Result.Succeeded;
                }

                // 4. Ruta a ESTA DLL
                string dllPath = Assembly.GetExecutingAssembly().Location;
                string exeDirectory = Path.GetDirectoryName(dllPath);

                // -----------------------------------------------------------------
                // PRIMER BOTÓN: "TAG ILU"
                // -----------------------------------------------------------------
                PushButtonData buttonData1 = new PushButtonData(
                    "MyButtonInfo",                 // Nombre interno
                    "Tag Ilu",                     // Texto que se ve en la cinta
                    dllPath,                       // Ruta de la DLL
                    "MiNamespace.MyTAGS_ILU"      // Clase IExternalCommand asociada
                );

                // Cargar la imagen del primer botón
                string iconPath1 = Path.Combine(exeDirectory, "Images", "tags.png");
                if (File.Exists(iconPath1))
                {
                    BitmapImage largeImage1 = new BitmapImage();
                    largeImage1.BeginInit();
                    largeImage1.UriSource = new Uri(iconPath1, UriKind.Absolute);
                    largeImage1.EndInit();

                    buttonData1.LargeImage = largeImage1;
                    buttonData1.Image = largeImage1;
                }
                // Agregar el primer botón al panel
                panel.AddItem(buttonData1);

                // -----------------------------------------------------------------
                // SEGUNDO BOTÓN: "TAG ELE"
                // -----------------------------------------------------------------
                PushButtonData buttonData2 = new PushButtonData(
                    "MyButtonInfo2",                   // Nombre interno
                    "Tag Alimentadores",               // Texto que se ve en la cinta
                    dllPath,                           // Ruta de la DLL
                    "MiNamespace.MyTAGS_ELE"     // Clase IExternalCommand asociada
                );
                // Cargar la imagen del primer botón
                string iconPath2 = Path.Combine(exeDirectory, "Images", "tags.png");
                if (File.Exists(iconPath1))
                {
                    BitmapImage largeImage2 = new BitmapImage();
                    largeImage2.BeginInit();
                    largeImage2.UriSource = new Uri(iconPath2, UriKind.Absolute);
                    largeImage2.EndInit();

                    buttonData2.LargeImage = largeImage2;
                    buttonData2.Image = largeImage2;
                }
                // Agregar el primer botón al panel
                panel.AddItem(buttonData2);

                // -----------------------------------------------------------------
                // TERCER BOTÓN: "TAG TOMAS"
                // -----------------------------------------------------------------
                PushButtonData buttonData3 = new PushButtonData(
                    "MyButtonInfo3",                   // Nombre interno
                    "Tag Tomas",               // Texto que se ve en la cinta
                    dllPath,                           // Ruta de la DLL
                    "MiNamespace.MyTAGS_Tomas"     // Clase IExternalCommand asociada
                );
                // Cargar la imagen del primer botón
                string iconPath3 = Path.Combine(exeDirectory, "Images", "tomas.png");
                if (File.Exists(iconPath3))
                {
                    BitmapImage largeImage3 = new BitmapImage();
                    largeImage3.BeginInit();
                    largeImage3.UriSource = new Uri(iconPath3, UriKind.Absolute);
                    largeImage3.EndInit();

                    buttonData3.LargeImage = largeImage3;
                    buttonData3.Image = largeImage3;
                }
                // Agregar el primer botón al panel
                panel.AddItem(buttonData3);

                // -----------------------------------------------------------------
                // CUARTO BOTÓN: "Alumbrado"
                // -----------------------------------------------------------------
                PushButtonData buttonData4 = new PushButtonData(
                    "MyButtonInfo4",                   // Nombre interno
                    "Tag Alumbrado",               // Texto que se ve en la cinta
                    dllPath,                           // Ruta de la DLL
                    "MiNamespace.MyTAGS_Alumbrado"     // Clase IExternalCommand asociada
                );
                // Cargar la imagen del primer botón
                string iconPath4 = Path.Combine(exeDirectory, "Images", "tags.png");
                if (File.Exists(iconPath4))
                {
                    BitmapImage largeImage4 = new BitmapImage();
                    largeImage4.BeginInit();
                    largeImage4.UriSource = new Uri(iconPath4, UriKind.Absolute);
                    largeImage4.EndInit();

                    buttonData4.LargeImage = largeImage4;
                    buttonData4.Image = largeImage4;
                }
                // Agregar el primer botón al panel
                panel.AddItem(buttonData4);

                // -----------------------------------------------------------------
                // QUINTO BOTÓN: "PREDIMENSIONAMIENTO"
                // -----------------------------------------------------------------
                PushButtonData buttonData5 = new PushButtonData(
                    "MyButtonInfo5",                   // Nombre interno
                    "Pre.Dim",                         // Texto que se ve en la cinta
                    dllPath,                           // Ruta de la DLL
                    "MiNamespace.MyCommandPreDim"      // Clase IExternalCommand asociada
                );
                // Cargar la imagen del primer botón
                string iconPath5 = Path.Combine(exeDirectory, "Images", "predim.png");
                if (File.Exists(iconPath5))
                {
                    BitmapImage largeImage5 = new BitmapImage();
                    largeImage5.BeginInit();
                    largeImage5.UriSource = new Uri(iconPath5, UriKind.Absolute);
                    largeImage5.EndInit();

                    buttonData5.LargeImage = largeImage5;
                    buttonData5.Image = largeImage5;
                }
                // Agregar el primer botón al panel
                panel.AddItem(buttonData5);

                //// -----------------------------------------------------------------
                //// QUINTO BOTÓN: "Arañas"
                //// -----------------------------------------------------------------
                //PushButtonData buttonData5 = new PushButtonData(
                //    "MyButtonInfo5",                   // Nombre interno
                //    "Araña",                             // Texto que se ve en la cinta
                //    dllPath,                           // Ruta de la DLL
                //    "MiNamespace.MyAraña"              // Clase IExternalCommand asociada
                //);
                //// Cargar la imagen del primer botón
                //string iconPath5 = Path.Combine(exeDirectory, "Images", "tags.png");
                //if (File.Exists(iconPath5))
                //{
                //    BitmapImage largeImage5 = new BitmapImage();
                //    largeImage5.BeginInit();
                //    largeImage5.UriSource = new Uri(iconPath5, UriKind.Absolute);
                //    largeImage5.EndInit();

                //    buttonData5.LargeImage = largeImage5;
                //    buttonData5.Image = largeImage5;
                //}
                //// Agregar el primer botón al panel
                //panel.AddItem(buttonData5);


                // -----------------------------------------------------------------
                // SEXTO BOTÓN: "SIPRA"
                // -----------------------------------------------------------------
                PushButtonData buttonData6 = new PushButtonData(
                    "MyButtonInfo6",                   // Nombre interno
                    "Tag SIPRA",                             // Texto que se ve en la cinta
                    dllPath,                           // Ruta de la DLL
                    "MiNamespace.MyTAGS_SIPRA"              // Clase IExternalCommand asociada
                );
                // Cargar la imagen del primer botón
                string iconPath6 = Path.Combine(exeDirectory, "Images", "tags.png");
                if (File.Exists(iconPath6))
                {
                    BitmapImage largeImage6 = new BitmapImage();
                    largeImage6.BeginInit();
                    largeImage6.UriSource = new Uri(iconPath6, UriKind.Absolute);
                    largeImage6.EndInit();
                    buttonData6.LargeImage = largeImage6;
                    buttonData6.Image = largeImage6;
                }

                // Agregar el primer botón al panel
                panel.AddItem(buttonData6);

                // -----------------------------------------------------------------
                // SEPTIMO BOTÓN: "HVAC"
                // -----------------------------------------------------------------

                PushButtonData buttonData7 = new PushButtonData(
                    "MyButtonInfo7",                   // Nombre interno
                    "Tag HVAC",                        // Texto que se ve en la cinta
                    dllPath,                           // Ruta de la DLL
                    "MiNamespace.MyTAGS_HVAC"          // Clase IExternalCommand asociada
                );
                // Cargar la imagen del primer botón
                string iconPath7 = Path.Combine(exeDirectory, "Images", "tags.png");
                if (File.Exists(iconPath7))
                {
                    BitmapImage largeImage7 = new BitmapImage();
                    largeImage7.BeginInit();
                    largeImage7.UriSource = new Uri(iconPath7, UriKind.Absolute);
                    largeImage7.EndInit();
                    buttonData7.LargeImage = largeImage7;
                    buttonData7.Image = largeImage7;
                }
                // Agregar el primer botón al panel
                panel.AddItem(buttonData7);

                // -----------------------------------------------------------------
                // OCTAVO BOTÓN "HVAC"
                // -----------------------------------------------------------------

                PushButtonData buttonData8 = new PushButtonData(
                    "MyButtonInfo8",                   // Nombre interno
                    "HVAC Test",                        // Texto que se ve en la cinta
                    dllPath,                           // Ruta de la DLL
                    "MiNamespace.MyTAGS_HVAC2"          // Clase IExternalCommand asociada
                );
                // Cargar la imagen del primer botón
                string iconPath8 = Path.Combine(exeDirectory, "Images", "hvac.png");
                if (File.Exists(iconPath8))
                {
                    BitmapImage largeImage8 = new BitmapImage();
                    largeImage8.BeginInit();
                    largeImage8.UriSource = new Uri(iconPath8, UriKind.Absolute);
                    largeImage8.EndInit();
                    buttonData8.LargeImage = largeImage8;
                    buttonData8.Image = largeImage8;
                }
                // Agregar el primer botón al panel
                panel.AddItem(buttonData8);

                // -----------------------------------------------------------------
                // NOVENO BOTÓN "HVAC"
                // -----------------------------------------------------------------

                PushButtonData buttonData9 = new PushButtonData(
                    "MyButtonInfo9",                   // Nombre interno
                    "HVAC Ruta c.",                        // Texto que se ve en la cinta
                    dllPath,                           // Ruta de la DLL
                    "MiNamespace.MyTAGS_HVAC_RC"          // Clase IExternalCommand asociada
                );
                // Cargar la imagen del primer botón
                string iconPath9 = Path.Combine(exeDirectory, "Images", "tags.png");
                if (File.Exists(iconPath9))
                {
                    BitmapImage largeImage9 = new BitmapImage();
                    largeImage9.BeginInit();
                    largeImage9.UriSource = new Uri(iconPath9, UriKind.Absolute);
                    largeImage9.EndInit();
                    buttonData9.LargeImage = largeImage9;
                    buttonData9.Image = largeImage9;
                }
                // Agregar el primer botón al panel
                panel.AddItem(buttonData9);

                // -----------------------------------------------------------------
                // DECIMO BOTÓN "COOR"
                // -----------------------------------------------------------------

                PushButtonData buttonData10 = new PushButtonData(
                    "MyButtonInfo10",                   // Nombre interno
                    "Tag Coor",                        // Texto que se ve en la cinta
                    dllPath,                           // Ruta de la DLL
                    "MiNamespace.MyTAG_Coor"          // Clase IExternalCommand asociada
                );
                // Cargar la imagen del primer botón
                string iconPath10 = Path.Combine(exeDirectory, "Images", "tags.png");
                if (File.Exists(iconPath10))
                {
                    BitmapImage largeImage10 = new BitmapImage();
                    largeImage10.BeginInit();
                    largeImage10.UriSource = new Uri(iconPath10, UriKind.Absolute);
                    largeImage10.EndInit();
                    buttonData10.LargeImage = largeImage10;
                    buttonData10.Image = largeImage10;
                }
                // Agregar el primer botón al panel
                panel.AddItem(buttonData10);


                // -----------------------------------------------------------------
                // BOTÓN 11: "ARQ TAGS"
                // -----------------------------------------------------------------

                PushButtonData buttonData11 = new PushButtonData(
                    "MyButtonInfo11",             // Nombre interno
                    "Tags Arq",                   // Texto que se ve en la cinta
                    dllPath,                      // Ruta de la DLL
                    "MiNamespace.MyTAGS_ARQ"      // Clase IExternalCommand asociada
                );

                // Cargar imagen
                string iconPath11 = Path.Combine(exeDirectory, "Images", "tags.png");

                if (File.Exists(iconPath11))
                {
                    BitmapImage largeImage11 = new BitmapImage();
                    largeImage11.BeginInit();
                    largeImage11.UriSource = new Uri(iconPath11, UriKind.Absolute);
                    largeImage11.EndInit();

                    buttonData11.LargeImage = largeImage11;
                    buttonData11.Image = largeImage11;
                }

                // Agregar botón al panel
                panel.AddItem(buttonData11);



                // -----------------------------------------------------------------
                // BOTÓN 12: "Cotas Arq1" — contenedor (PulldownButton) con 2 sub-comandos
                // compilados dentro de esta misma DLL (RevitPlugin.dll)
                // -----------------------------------------------------------------
                PulldownButtonData pulldownData13 = new PulldownButtonData(
                    "MyPulldown13",   // Nombre interno
                    "Cotas Arq"      // Texto en la cinta
                );

                string iconPath13 = Path.Combine(exeDirectory, "Images", "cotas.png");
                if (File.Exists(iconPath13))
                {
                    BitmapImage largeImage13 = new BitmapImage();
                    largeImage13.BeginInit();
                    largeImage13.UriSource = new Uri(iconPath13, UriKind.Absolute);
                    largeImage13.EndInit();
                    pulldownData13.LargeImage = largeImage13;
                    pulldownData13.Image = largeImage13;
                }

                if (!(panel.AddItem(pulldownData13) is PulldownButton pulldown13))
                    throw new InvalidOperationException("No se pudo crear el PulldownButton 'Cotas Arq1'.");

                // Sub-botón 1: Insertar cotas exteriores en planta
                PushButtonData subBtn13a = new PushButtonData(
                    "SubBtn_CotasExteriores",
                    "Acotar",
                    dllPath,
                    "PluginCotasExteriores.Commands.PluginCotasExterioresCommand"
                );

                // Sub-botón 2: Configuración de cotas
                PushButtonData subBtn13b = new PushButtonData(
                    "SubBtn_CotasConfig",
                    "Config. Cotas",
                    dllPath,
                    "PluginCotasExteriores.Commands.PluginCotasExterioresSettingsCommand"
                );

                pulldown13.AddPushButton(subBtn13a);
                pulldown13.AddPushButton(subBtn13b);


                // -----------------------------------------------------------------
                // BOTÓN 13: "TAGS, Cotas, Convenciones de Cielos Rasos"
                // -----------------------------------------------------------------

                PushButtonData buttonData13 = new PushButtonData(
                    "MyButtonInfo13",
                    "Cielos Rasos",
                    dllPath,
                    "MiNamespace.MyTAGS_CielosRasos"
                );

                // Cargar imagen
                string iconPath13_btn = Path.Combine(exeDirectory, "Images", "tags.png");

                if (File.Exists(iconPath13_btn))
                {
                    BitmapImage largeImage13 = new BitmapImage();
                    largeImage13.BeginInit();
                    largeImage13.UriSource = new Uri(iconPath13_btn, UriKind.Absolute);
                    largeImage13.EndInit();

                    buttonData13.LargeImage = largeImage13;
                    buttonData13.Image = largeImage13;
                }

                //  Agregar el botón 
                panel.AddItem(buttonData13);

                // -----------------------------------------------------------------
                // BOTÓN 14: "Generar Descripciones"
                // -----------------------------------------------------------------

                PushButtonData buttonData14 = new PushButtonData(
                    "MyButtonInfo14",                     // Nombre interno (único)
                    "Generar\nDescripciones",             // Texto (con salto de línea)
                    dllPath,
                    "MiNamespace.GenerateDescriptions"  // Clase que vamos a crear
                );

                // Icono
                string iconPath14 = Path.Combine(exeDirectory, "Images", "desc.png");

                if (File.Exists(iconPath14))
                {
                    BitmapImage largeImage14 = new BitmapImage();
                    largeImage14.BeginInit();
                    largeImage14.UriSource = new Uri(iconPath14, UriKind.Absolute);
                    largeImage14.EndInit();

                    buttonData14.LargeImage = largeImage14;
                    buttonData14.Image = largeImage14;
                }

                // Agregar al panel
                panel.AddItem(buttonData14);


                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
                return Result.Failed;
            }
        }

                


        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
