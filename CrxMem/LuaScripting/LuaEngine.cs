using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using NLua;
using CrxMem.Core;

namespace CrxMem.LuaScripting
{
    /// <summary>
    /// Core Lua engine that manages the Lua state and provides script execution capabilities.
    /// Modeled after CheatEngine's LuaHandler.pas implementation.
    /// </summary>
    public class LuaEngine : IDisposable
    {
        private Lua? _lua;
        private ProcessAccess? _processAccess;
        private readonly object _lockObject = new object();
        private bool _isExecuting;
        private CancellationTokenSource? _cancellationTokenSource;
        private MainWindow? _mainWindow;

        // Events for output and errors
        public event Action<string>? OnOutput;
        public event Action<string>? OnError;
        public event Action? OnScriptStarted;
        public event Action? OnScriptFinished;

        // Function registrations for each module
        private LuaMemoryFunctions? _memoryFunctions;
        private LuaProcessFunctions? _processFunctions;
        private LuaUtilityFunctions? _utilityFunctions;
        private LuaAddressListFunctions? _addressListFunctions;
        private LuaScanFunctions? _scanFunctions;
        private LuaAssemblerFunctions? _assemblerFunctions;
        private LuaDebugFunctions? _debugFunctions;
        private LuaGuiFunctions? _guiFunctions;

        public bool IsExecuting => _isExecuting;
        public ProcessAccess? ProcessAccess => _processAccess;
        public MainWindow? MainWindow => _mainWindow;

        public LuaEngine()
        {
            Initialize();
        }

        /// <summary>
        /// Initialize the Lua state and register all functions.
        /// </summary>
        private void Initialize()
        {
            lock (_lockObject)
            {
                _lua?.Dispose();
                _lua = new Lua();

                // Allow Lua to use CLR types
                _lua.LoadCLRPackage();

                // Register the print function to redirect to our output
                _lua.RegisterFunction("print", this, GetType().GetMethod(nameof(LuaPrint)));

                // Initialize and register all function modules
                RegisterFunctions();
            }
        }

        /// <summary>
        /// Register all Lua function modules.
        /// </summary>
        private void RegisterFunctions()
        {
            if (_lua == null) return;

            // Memory functions (readInteger, writeInteger, etc.)
            _memoryFunctions = new LuaMemoryFunctions(this);
            _memoryFunctions.Register(_lua);

            // Process functions (openProcess, getProcessList, etc.)
            _processFunctions = new LuaProcessFunctions(this);
            _processFunctions.Register(_lua);

            // Utility functions (showMessage, sleep, etc.)
            _utilityFunctions = new LuaUtilityFunctions(this);
            _utilityFunctions.Register(_lua);

            // Address list functions
            _addressListFunctions = new LuaAddressListFunctions(this);
            _addressListFunctions.Register(_lua);

            // Scan functions (AOBScan, etc.)
            _scanFunctions = new LuaScanFunctions(this);
            _scanFunctions.Register(_lua);

            // Assembler functions
            _assemblerFunctions = new LuaAssemblerFunctions(this);
            _assemblerFunctions.Register(_lua);

            // Debug functions
            _debugFunctions = new LuaDebugFunctions(this);
            _debugFunctions.Register(_lua);

            // GUI functions (createForm, createButton, etc.)
            _guiFunctions = new LuaGuiFunctions(this);
            _guiFunctions.Register(_lua);
        }

        /// <summary>
        /// Set the process access object for memory operations.
        /// </summary>
        public void SetProcessAccess(ProcessAccess? process)
        {
            _processAccess = process;
        }

        /// <summary>
        /// Set the main window reference for address list operations.
        /// </summary>
        public void SetMainWindow(MainWindow? mainWindow)
        {
            _mainWindow = mainWindow;
        }

        /// <summary>
        /// Execute a Lua script string.
        /// </summary>
        public void ExecuteScript(string script)
        {
            if (_lua == null || _isExecuting)
            {
                OnError?.Invoke("Engine not ready or script already executing");
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _isExecuting = true;
            OnScriptStarted?.Invoke();

            try
            {
                lock (_lockObject)
                {
                    _lua.DoString(script);
                }
                OnOutput?.Invoke("\n[Script completed successfully]");
            }
            catch (NLua.Exceptions.LuaException ex)
            {
                OnError?.Invoke($"Lua Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Error: {ex.Message}");
            }
            finally
            {
                _isExecuting = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                OnScriptFinished?.Invoke();
            }
        }

        /// <summary>
        /// Execute a Lua script from a file.
        /// </summary>
        public void ExecuteFile(string filePath)
        {
            if (_lua == null || _isExecuting)
            {
                OnError?.Invoke("Engine not ready or script already executing");
                return;
            }

            if (!System.IO.File.Exists(filePath))
            {
                OnError?.Invoke($"File not found: {filePath}");
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _isExecuting = true;
            OnScriptStarted?.Invoke();

            try
            {
                lock (_lockObject)
                {
                    _lua.DoFile(filePath);
                }
                OnOutput?.Invoke($"\n[Script '{System.IO.Path.GetFileName(filePath)}' completed successfully]");
            }
            catch (NLua.Exceptions.LuaException ex)
            {
                OnError?.Invoke($"Lua Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Error: {ex.Message}");
            }
            finally
            {
                _isExecuting = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                OnScriptFinished?.Invoke();
            }
        }

        /// <summary>
        /// Stop the currently executing script.
        /// </summary>
        public void StopScript()
        {
            _cancellationTokenSource?.Cancel();

            // Interrupt the Lua state (this may not always work cleanly)
            // For better interruption, scripts should check for cancellation periodically
            lock (_lockObject)
            {
                // Reinitialize the engine to ensure clean state
                if (_isExecuting)
                {
                    Initialize();
                    _isExecuting = false;
                    OnOutput?.Invoke("\n[Script stopped by user]");
                    OnScriptFinished?.Invoke();
                }
            }
        }

        /// <summary>
        /// Reset the Lua state (clears all variables and starts fresh).
        /// </summary>
        public void Reset()
        {
            if (_isExecuting)
            {
                StopScript();
            }
            Initialize();
            OnOutput?.Invoke("[Lua engine reset]");
        }

        /// <summary>
        /// Lua print function - redirects output to our event.
        /// </summary>
        public void LuaPrint(params object[] args)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append('\t');
                sb.Append(args[i]?.ToString() ?? "nil");
            }
            OnOutput?.Invoke(sb.ToString());
        }

        /// <summary>
        /// Get a global variable from the Lua state.
        /// </summary>
        public object? GetGlobal(string name)
        {
            lock (_lockObject)
            {
                return _lua?[name];
            }
        }

        /// <summary>
        /// Set a global variable in the Lua state.
        /// </summary>
        public void SetGlobal(string name, object? value)
        {
            lock (_lockObject)
            {
                if (_lua != null)
                {
                    _lua[name] = value;
                }
            }
        }

        /// <summary>
        /// Check if cancellation has been requested.
        /// </summary>
        public bool IsCancellationRequested => _cancellationTokenSource?.IsCancellationRequested ?? false;

        public void Dispose()
        {
            StopScript();
            _guiFunctions?.Dispose();
            lock (_lockObject)
            {
                _lua?.Dispose();
                _lua = null;
            }
        }
    }
}
