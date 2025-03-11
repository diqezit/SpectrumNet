namespace SpectrumNet
{
    public partial class ControlPanelWindow : Window
    {
        private readonly MainWindow _mainWindow;

        public ControlPanelWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            InitializeComponent();
            Loaded += (s, e) => DataContext = _mainWindow;
        }

        /// <summary>
        /// Переадресует клики по кнопкам в MainWindow для обработки.
        /// </summary>
        private void OnButtonClick(object sender, RoutedEventArgs e)
        {
            _mainWindow.OnButtonClick(sender, e);
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
                DragMove();
            }
        }
    }
}