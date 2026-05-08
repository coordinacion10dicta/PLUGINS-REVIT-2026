namespace PluginCotasExteriores.Configurations
{
    using System.Xml.Linq;
    using Helpers;

    /// <summary>Dimension chain configuration</summary>
    public class ExteriorDimensionChain : ObservableObject
    {
        private string _displayName;
        private int _elementOffset;
        private bool _walls;
        private bool _openings;
        private System.Windows.Visibility _intersectingWallsAndOpeningsVisibility;
        private bool _grids;
        private bool _extremeGrids;
        private bool _overall;

        public ExteriorDimensionChain()
        {
            _walls = true;
            _openings = false;
            _grids = false;
            _elementOffset = 8;
            _extremeGrids = false;
        }
        
        public string DisplayName
        {
            get => _displayName;
            set
            {
                _displayName = value;
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        /// <summary>Offset from previous element (wall or other chain) in mm</summary>
        public int ElementOffset
        {
            get => _elementOffset;
            set
            {
                _elementOffset = value;
                OnPropertyChanged(nameof(ElementOffset));
            }
        }

        /// <summary>Dimension by walls</summary>
        public bool Walls
        {
            get => _walls;
            set
            {
                _walls = value;
                OnPropertyChanged(nameof(Walls));
                if (value)
                {
                    IntersectingWallsAndOpeningsVisibility = System.Windows.Visibility.Visible;
                    if (Overall)
                    {
                        Overall = false;
                        OnPropertyChanged(nameof(Overall));
                    }

                    if (ExtremeGrids)
                    {
                        ExtremeGrids = false;
                        OnPropertyChanged(nameof(ExtremeGrids));
                    }
                }
                else
                {
                    if (IntersectingWalls)
                    {
                        IntersectingWalls = false;
                        OnPropertyChanged(nameof(IntersectingWalls));
                    }

                    if (Openings)
                    {
                        Openings = false;
                        OnPropertyChanged(nameof(Openings));
                    }

                    IntersectingWallsAndOpeningsVisibility = System.Windows.Visibility.Hidden;
                    if (!Grids && !Overall)
                    {
                        Overall = true;
                        OnPropertyChanged(nameof(Overall));
                    }
                }
            }
        }

        /// <summary>Dimension by intersecting walls</summary>
        public bool IntersectingWalls
        {
            get => false;
            set
            {
                // Temporarily disabled.
                OnPropertyChanged(nameof(IntersectingWalls));
            }
        }

        /// <summary>Dimension by openings</summary>
        public bool Openings
        {
            get => _openings;
            set
            {
                _openings = value;
                OnPropertyChanged(nameof(Openings));
            }
        }

        public System.Windows.Visibility IntersectingWallsAndOpeningsVisibility
        {
            get => _intersectingWallsAndOpeningsVisibility; set
            {
                _intersectingWallsAndOpeningsVisibility = value;
                OnPropertyChanged(nameof(IntersectingWallsAndOpeningsVisibility));
            }
        }

        /// <summary>Dimension by grids</summary>
        public bool Grids
        {
            get => _grids;
            set
            {
                _grids = value;
                if (value)
                {
                    if (ExtremeGrids)
                    {
                        ExtremeGrids = false;
                        OnPropertyChanged(nameof(ExtremeGrids));
                    }

                    if (Overall)
                    {
                        Overall = false;
                        OnPropertyChanged(nameof(Overall));
                    }
                }

                OnPropertyChanged(nameof(Grids));
            }
        }

        public bool ExtremeGrids
        {
            get => false;
            set
            {
                // Temporarily disabled.
                _extremeGrids = false;
                OnPropertyChanged(nameof(ExtremeGrids));
                if (_extremeGrids)
                {
                    if (Walls)
                    {
                        Walls = false;
                        OnPropertyChanged(nameof(Walls));
                    }

                    if (Openings)
                    {
                        Openings = false;
                        OnPropertyChanged(nameof(Openings));
                    }

                    if (IntersectingWalls)
                    {
                        IntersectingWalls = false;
                        OnPropertyChanged(nameof(IntersectingWalls));
                    }

                    if (Grids)
                    {
                        Grids = false;
                        OnPropertyChanged(nameof(Grids));
                    }

                    if (Overall)
                    {
                        Overall = false;
                        OnPropertyChanged(nameof(Overall));
                    }
                }
                else
                {
                    if (!Walls && !Overall && !Grids)
                    {
                        Walls = true;
                        OnPropertyChanged(nameof(Walls));
                    }
                }
            }
        }

        public bool Overall
        {
            get => _overall;
            set
            {
                _overall = value;
                OnPropertyChanged(nameof(Overall));
                if (value)
                {
                    if (Walls)
                    {
                        Walls = false;
                        OnPropertyChanged(nameof(Walls));
                    }

                    if (Openings)
                    {
                        Openings = false;
                        OnPropertyChanged(nameof(Openings));
                    }

                    if (IntersectingWalls)
                    {
                        IntersectingWalls = false;
                        OnPropertyChanged(nameof(IntersectingWalls));
                    }

                    if (Grids)
                    {
                        Grids = false;
                        OnPropertyChanged(nameof(Grids));
                    }

                    if (ExtremeGrids)
                    {
                        ExtremeGrids = false;
                        OnPropertyChanged(nameof(ExtremeGrids));
                    }
                }
                else
                {
                    if (!Walls && !ExtremeGrids && !Grids)
                    {
                        Walls = true;
                        OnPropertyChanged(nameof(Walls));
                    }
                }
            }
        }
        
        /// <summary>Deserialize from XElement</summary>
        public static ExteriorDimensionChain GetExteriorDimensionChainFromXElement(XElement xElement)
        {
            var edc = new ExteriorDimensionChain();

            // bools
            edc.Walls = !bool.TryParse(xElement.Attribute(nameof(edc.Walls))?.Value, out var b) || b;
            edc.Grids = bool.TryParse(xElement.Attribute(nameof(edc.Grids))?.Value, out b) && b;
            edc.Openings = bool.TryParse(xElement.Attribute(nameof(edc.Openings))?.Value, out b) && b;
            edc.IntersectingWalls = false;
            edc.ExtremeGrids = false;
            edc.Overall = bool.TryParse(xElement.Attribute(nameof(edc.Overall))?.Value, out b) && b;

            // ints
            edc.ElementOffset = int.TryParse(xElement.Attribute(nameof(edc.ElementOffset))?.Value, out var i) ? i : 8;

            return edc;
        }

        /// <summary>Serialize to XElement</summary>
        public XElement GetXElementFromDimensionChainInstance()
        {
            var xElement = new XElement(Constants.XElementName_Chain);
            xElement.SetAttributeValue(nameof(Walls), Walls);
            xElement.SetAttributeValue(nameof(IntersectingWalls), IntersectingWalls);
            xElement.SetAttributeValue(nameof(Openings), Openings);
            xElement.SetAttributeValue(nameof(Grids), Grids);
            xElement.SetAttributeValue(nameof(ExtremeGrids), ExtremeGrids);
            xElement.SetAttributeValue(nameof(ElementOffset), ElementOffset);
            xElement.SetAttributeValue(nameof(Overall), Overall);

            return xElement;
        }
    }
}
