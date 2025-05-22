// IUIController.cs
namespace SpectrumNet.SN.Controllers.Interfaces;

public interface IUIController
{

    // Методы управления панелью
    void OpenControlPanel();
    void CloseControlPanel();
    void ToggleControlPanel();

    // Методы управления оверлеем
    void OpenOverlay();
    void CloseOverlay();
    bool IsOverlayActive { get; set; }
    bool IsOverlayTopmost { get; set; }

    // Управление темой
    void ToggleTheme();

    // UI свойства
    bool IsControlPanelOpen { get; }
    bool IsPopupOpen { get; set; }

    // Уведомления об изменениях
    void OnPropertyChanged(params string[] propertyNames);


}