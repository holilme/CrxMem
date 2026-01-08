using System;
using System.Threading;
using System.Windows.Forms;
using NLua;

namespace CrxMem.LuaScripting
{
    /// <summary>
    /// Utility functions for Lua scripts.
    /// Matches CheatEngine's utility API.
    /// </summary>
    public class LuaUtilityFunctions
    {
        private readonly LuaEngine _engine;

        public LuaUtilityFunctions(LuaEngine engine)
        {
            _engine = engine;
        }

        public void Register(Lua lua)
        {
            lua.RegisterFunction("showMessage", this, GetType().GetMethod(nameof(ShowMessage)));
            lua.RegisterFunction("inputQuery", this, GetType().GetMethod(nameof(InputQuery)));
            lua.RegisterFunction("sleep", this, GetType().GetMethod(nameof(Sleep)));
            lua.RegisterFunction("getTickCount", this, GetType().GetMethod(nameof(GetTickCount)));
            lua.RegisterFunction("messageDialog", this, GetType().GetMethod(nameof(MessageDialog)));
            lua.RegisterFunction("playSound", this, GetType().GetMethod(nameof(PlaySound)));
            lua.RegisterFunction("beep", this, GetType().GetMethod(nameof(Beep)));
            lua.RegisterFunction("getCEVersion", this, GetType().GetMethod(nameof(GetCEVersion)));
            lua.RegisterFunction("getCheatEngineVersion", this, GetType().GetMethod(nameof(GetCEVersion)));

            // Register CheatEngine constants for compatibility
            RegisterConstants(lua);
        }

        /// <summary>
        /// Register CheatEngine-compatible constants as Lua globals.
        /// </summary>
        private void RegisterConstants(Lua lua)
        {
            // BorderStyle constants (matches CE defines.lua)
            lua["bsNone"] = 0;
            lua["bsSingle"] = 1;
            lua["bsSizeable"] = 2;
            lua["bsDialog"] = 3;
            lua["bsToolWindow"] = 4;
            lua["bsSizeToolWin"] = 5;

            // Position constants (matches CE defines.lua)
            lua["poDesigned"] = 0;
            lua["poDefault"] = 1;
            lua["poDefaultPosOnly"] = 2;
            lua["poDefaultSizeOnly"] = 3;
            lua["poScreenCenter"] = 4;
            lua["poDesktopCenter"] = 5;
            lua["poMainFormCenter"] = 6;
            lua["poOwnerFormCenter"] = 7;

            // MessageBox button constants
            lua["mbYes"] = 0;
            lua["mbNo"] = 1;
            lua["mbOK"] = 2;
            lua["mbCancel"] = 3;
            lua["mbAbort"] = 4;
            lua["mbRetry"] = 5;
            lua["mbIgnore"] = 6;
            lua["mbAll"] = 7;
            lua["mbNoToAll"] = 8;
            lua["mbYesToAll"] = 9;
            lua["mbHelp"] = 10;

            // MessageBox type constants
            lua["mtWarning"] = 0;
            lua["mtError"] = 1;
            lua["mtInformation"] = 2;
            lua["mtConfirmation"] = 3;
            lua["mtCustom"] = 4;

            // DialogResult constants
            lua["mrNone"] = 0;
            lua["mrOk"] = 1;
            lua["mrCancel"] = 2;
            lua["mrAbort"] = 3;
            lua["mrRetry"] = 4;
            lua["mrIgnore"] = 5;
            lua["mrYes"] = 6;
            lua["mrNo"] = 7;
            lua["mrAll"] = 8;
            lua["mrNoToAll"] = 9;
            lua["mrYesToAll"] = 10;
        }

        /// <summary>
        /// Show a message box to the user.
        /// </summary>
        public void ShowMessage(string text)
        {
            // Marshal to UI thread if needed
            if (Application.OpenForms.Count > 0 && Application.OpenForms[0].InvokeRequired)
            {
                Application.OpenForms[0].Invoke(new Action(() =>
                {
                    MessageBox.Show(text, "CrxMem Script", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
            }
            else
            {
                MessageBox.Show(text, "CrxMem Script", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// Show an input dialog and return the user's input.
        /// </summary>
        public string InputQuery(string caption, string prompt, string defaultValue = "")
        {
            string result = defaultValue;

            // Marshal to UI thread if needed
            Action showDialog = () =>
            {
                using (var form = new Form())
                {
                    form.Text = caption;
                    form.FormBorderStyle = FormBorderStyle.FixedDialog;
                    form.MaximizeBox = false;
                    form.MinimizeBox = false;
                    form.StartPosition = FormStartPosition.CenterScreen;
                    form.Width = 400;
                    form.Height = 150;

                    var label = new Label
                    {
                        Text = prompt,
                        Left = 10,
                        Top = 10,
                        Width = 370
                    };

                    var textBox = new TextBox
                    {
                        Text = defaultValue,
                        Left = 10,
                        Top = 35,
                        Width = 360
                    };

                    var btnOk = new Button
                    {
                        Text = "OK",
                        DialogResult = DialogResult.OK,
                        Left = 210,
                        Top = 70,
                        Width = 75
                    };

                    var btnCancel = new Button
                    {
                        Text = "Cancel",
                        DialogResult = DialogResult.Cancel,
                        Left = 295,
                        Top = 70,
                        Width = 75
                    };

                    form.Controls.AddRange(new Control[] { label, textBox, btnOk, btnCancel });
                    form.AcceptButton = btnOk;
                    form.CancelButton = btnCancel;

                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        result = textBox.Text;
                    }
                }
            };

            if (Application.OpenForms.Count > 0 && Application.OpenForms[0].InvokeRequired)
            {
                Application.OpenForms[0].Invoke(showDialog);
            }
            else
            {
                showDialog();
            }

            return result;
        }

        /// <summary>
        /// Sleep for the specified number of milliseconds.
        /// </summary>
        public void Sleep(int milliseconds)
        {
            // Check for cancellation periodically during long sleeps
            if (milliseconds > 100)
            {
                int remaining = milliseconds;
                while (remaining > 0 && !_engine.IsCancellationRequested)
                {
                    int sleepTime = Math.Min(remaining, 100);
                    Thread.Sleep(sleepTime);
                    remaining -= sleepTime;
                }
            }
            else
            {
                Thread.Sleep(milliseconds);
            }
        }

        /// <summary>
        /// Get the current tick count (milliseconds since system start).
        /// </summary>
        public long GetTickCount()
        {
            return Environment.TickCount64;
        }

        /// <summary>
        /// Show a message dialog with custom type and buttons.
        /// Type: 0=info, 1=warning, 2=error, 3=question
        /// Buttons: 0=OK, 1=OKCancel, 2=YesNo, 3=YesNoCancel
        /// Returns: 0=None, 1=OK, 2=Cancel, 6=Yes, 7=No
        /// </summary>
        public int MessageDialog(string text, int type = 0, int buttons = 0)
        {
            MessageBoxIcon icon = type switch
            {
                0 => MessageBoxIcon.Information,
                1 => MessageBoxIcon.Warning,
                2 => MessageBoxIcon.Error,
                3 => MessageBoxIcon.Question,
                _ => MessageBoxIcon.Information
            };

            MessageBoxButtons btns = buttons switch
            {
                0 => MessageBoxButtons.OK,
                1 => MessageBoxButtons.OKCancel,
                2 => MessageBoxButtons.YesNo,
                3 => MessageBoxButtons.YesNoCancel,
                _ => MessageBoxButtons.OK
            };

            DialogResult result = DialogResult.None;

            Action showDialog = () =>
            {
                result = MessageBox.Show(text, "CrxMem Script", btns, icon);
            };

            if (Application.OpenForms.Count > 0 && Application.OpenForms[0].InvokeRequired)
            {
                Application.OpenForms[0].Invoke(showDialog);
            }
            else
            {
                showDialog();
            }

            return (int)result;
        }

        /// <summary>
        /// Play a system sound.
        /// </summary>
        public void PlaySound(string soundType = "default")
        {
            switch (soundType.ToLower())
            {
                case "asterisk":
                    System.Media.SystemSounds.Asterisk.Play();
                    break;
                case "beep":
                    System.Media.SystemSounds.Beep.Play();
                    break;
                case "exclamation":
                    System.Media.SystemSounds.Exclamation.Play();
                    break;
                case "hand":
                    System.Media.SystemSounds.Hand.Play();
                    break;
                case "question":
                    System.Media.SystemSounds.Question.Play();
                    break;
                default:
                    System.Media.SystemSounds.Beep.Play();
                    break;
            }
        }

        /// <summary>
        /// Play a beep sound.
        /// </summary>
        public void Beep()
        {
            Console.Beep();
        }

        /// <summary>
        /// Get the CrxMem version (for compatibility with CE scripts).
        /// </summary>
        public string GetCEVersion()
        {
            return "7.5"; // Return CE-compatible version for script compatibility
        }
    }
}
