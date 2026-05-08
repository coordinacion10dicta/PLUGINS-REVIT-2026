namespace PluginCotasExteriores.Configurations
{
    using System;
    using Helpers;

    /// <summary>Base configuration class</summary>
    public class BaseConfiguration : ObservableObject
    {
        private string _name;

        /// <summary>Identifier</summary>
        public Guid Id { get; set; }

        /// <summary>Configuration name</summary>
        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    }
}
