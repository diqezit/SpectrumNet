namespace SpectrumNet
{
    public partial class ControlPanelWindow : System.Windows.Window, IDisposable
    {
        private readonly MainWindow _mainWindow;
        private bool _isDisposed;

        public ControlPanelWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            InitializeComponent();
            Loaded += (s, e) => DataContext = _mainWindow;
            MouseDoubleClick += OnWindowMouseDoubleClick;
        }

        /// <summary>
        /// Обработка двойного клика для разворачивания/сворачивания окна
        /// </summary>
        private void OnWindowMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.OriginalSource is DependencyObject element)
                {
                    if (IsControlOrChild(element, typeof(Slider)) ||
                        IsControlOrChild(element, typeof(ComboBox)) ||
                        IsControlOrChild(element, typeof(CheckBox)) ||
                        IsControlOrChild(element, typeof(Button)))
                    {
                        return;
                    }
                }

                // Переключаем состояние окна между нормальным и максимизированным
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;

                e.Handled = true;
            }
        }

        /// <summary>
        /// Проверяет, является ли элемент или его родитель элементом указанного типа
        /// </summary>
        private bool IsControlOrChild(DependencyObject element, Type controlType)
        {
            while (element != null)
            {
                if (controlType.IsInstanceOfType(element))
                    return true;

                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        /// <summary>
        /// Переадресует клики по кнопкам в MainWindow для обработки.
        /// </summary>
        private void OnButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            try
            {
                switch (btn.Name)
                {
                    case "CloseButton":
                        Close();
                        break;
                    default:
                        _mainWindow.OnButtonClick(sender, e);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in OnButtonClick: {ex.Message}");
            }
        }

        /// <summary>
        /// Переадресует изменения значений слайдеров в MainWindow.
        /// </summary>
        private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded || _mainWindow == null) return;
            _mainWindow.OnSliderValueChanged(sender, e);
        }

        /// <summary>
        /// Переадресует изменения выбора в комбобоксах в MainWindow.
        /// </summary>
        private void OnComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _mainWindow.OnComboBoxSelectionChanged(sender, e);
        }

        /// <summary>
        /// Обработка перетаскивания
        /// </summary>
        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                var mousePos = e.GetPosition(TitleBar);
                var closeButtonBounds = CloseButton.TransformToAncestor(TitleBar)
                    .TransformBounds(new Rect(0, 0, CloseButton.ActualWidth, CloseButton.ActualHeight));

                if (!closeButtonBounds.Contains(mousePos))
                {
                    try
                    {
                        DragMove();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in DragMove: {ex.Message}");
                    }
                }
            }
        }

        // Реализация IDisposable
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;

            if (disposing)
            {
                MouseDoubleClick -= OnWindowMouseDoubleClick;
                Loaded -= (s, e) => DataContext = _mainWindow;
            }

            _isDisposed = true;
        }

        ~ControlPanelWindow()
        {
            Dispose(false);
        }
    }
}