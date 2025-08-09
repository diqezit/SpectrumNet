#nullable enable

namespace SpectrumNet.SN.Controllers;

public sealed class BatchOperationsManager : IBatchOperationsManager
{
    private const string LogPrefix = nameof(BatchOperationsManager);

    private const int
        BATCH_UPDATE_INTERVAL_MS = 8,
        MAX_OPERATIONS_PER_FRAME = 10;

    private readonly ConcurrentQueue<Action> _pendingUIOperations = new();
    private readonly ISmartLogger _logger = Instance;
    private DispatcherTimer? _batchUpdateTimer;
    private bool _isDisposed;

    public BatchOperationsManager()
    {
        StartBatchUpdate();
    }

    public void EnqueueOperation(Action operation)
    {
        if (_isDisposed) return;
        _pendingUIOperations.Enqueue(operation);
    }

    public void ExecuteOrEnqueue(Action operation)
    {
        if (_isDisposed) return;

        if (_batchUpdateTimer == null || !_batchUpdateTimer.IsEnabled)
        {
            try
            {
                operation();
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error,
                    LogPrefix,
                    $"Error executing operation directly: {ex.Message}");
            }
        }
        else
        {
            EnqueueOperation(operation);
        }
    }

    private void StartBatchUpdate()
    {
        _batchUpdateTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = FromMilliseconds(BATCH_UPDATE_INTERVAL_MS)
        };
        _batchUpdateTimer.Tick += ProcessBatchedUIOperations;
        _batchUpdateTimer.Start();
    }

    private void ProcessBatchedUIOperations(object? sender, EventArgs e)
    {
        if (_isDisposed) return;

        int processedCount = 0;

        while (processedCount < MAX_OPERATIONS_PER_FRAME &&
               _pendingUIOperations.TryDequeue(out var operation))
        {
            try
            {
                operation();
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error,
                    LogPrefix,
                    $"Error in UI operation: {ex.Message}");
            }
            processedCount++;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_batchUpdateTimer != null)
        {
            _batchUpdateTimer.Stop();
            _batchUpdateTimer.Tick -= ProcessBatchedUIOperations;
            _batchUpdateTimer = null;
        }

        while (_pendingUIOperations.TryDequeue(out _)) { }
    }
}