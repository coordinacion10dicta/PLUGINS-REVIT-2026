using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace MiNamespace.UI
{
    public partial class UiGenerateDescriptions : Window
    {
        public string DisciplinaSeleccionada { get; private set; }
        public bool OpenLearningViewRequested { get; private set; }

        private List<string> Disciplinas;

        public UiGenerateDescriptions()
        {
            InitializeComponent();

            InicializarDisciplinas();
            RenderizarBotones();
        }

        // =========================================================
        // DISCIPLINAS DEFINIDAS EN CÓDIGO
        // =========================================================
        private void InicializarDisciplinas()
        {
            Disciplinas = new List<string>
            {
                "HVAC",
                "ELECTRICO",
                "HIDRAULICO",
                "ARQUITECTURA"
            };
        }

        // =========================================================
        // UI DINÁMICA
        // =========================================================
        private void RenderizarBotones()
        {
            panelDisciplinas.Children.Clear();

            foreach (string disc in Disciplinas)
            {
                Button btn = new Button
                {
                    Content = disc,
                    Width = 110,
                    Height = 40,
                    Margin = new Thickness(5)
                };

                btn.Click += (s, e) =>
                {
                    DisciplinaSeleccionada = disc;
                    DialogResult = true;
                    Close();
                };

                panelDisciplinas.Children.Add(btn);
            }
        }

        private void BtnLearningView_Click(object sender, RoutedEventArgs e)
        {
            OpenLearningViewRequested = true;
            DialogResult = false;
            Close();
        }
    }
}
