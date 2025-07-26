/*
DS4Windows
Copyright (C) 2023  Travis Nickles

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DS4Windows
{
    public enum MacroActionType
    {
        KeyPress,
        KeyRelease,
        KeyHold,
        MouseClick,
        MouseMove,
        Delay,
        ControllerInput,
        Condition,
        Loop,
        Variable,
        Function
    }

    public enum MacroConditionType
    {
        BatteryLevel,
        ButtonState,
        StickPosition,
        TriggerValue,
        TimeBased,
        VariableValue,
        GameState,
        SystemState
    }

    public class MacroAction
    {
        public MacroActionType Type { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
        public TimeSpan Duration { get; set; }
        public string Description { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    public class MacroCondition
    {
        public MacroConditionType Type { get; set; }
        public string Parameter { get; set; }
        public object ExpectedValue { get; set; }
        public string Operator { get; set; } = "equals"; // equals, greater, less, contains, etc.
        public bool IsInverted { get; set; } = false;
    }

    public class MacroSequence
    {
        public string Name { get; set; }
        public List<MacroAction> Actions { get; set; } = new List<MacroAction>();
        public List<MacroCondition> Conditions { get; set; } = new List<MacroCondition>();
        public int LoopCount { get; set; } = 1;
        public bool LoopIndefinitely { get; set; } = false;
        public TimeSpan Cooldown { get; set; } = TimeSpan.Zero;
        public DateTime LastExecuted { get; set; }
        public bool IsRunning { get; set; } = false;
        public int Priority { get; set; } = 1;
        public Dictionary<string, object> Variables { get; set; } = new Dictionary<string, object>();
    }

    public class MacroTrigger
    {
        public string Name { get; set; }
        public List<string> ButtonCombination { get; set; } = new List<string>();
        public bool RequireHold { get; set; } = false;
        public TimeSpan HoldDuration { get; set; } = TimeSpan.FromMilliseconds(500);
        public string AssociatedSequence { get; set; }
        public bool IsEnabled { get; set; } = true;
        public DateTime LastTriggered { get; set; }
    }

    public class AdvancedMacroSystem
    {
        private readonly DS4Device device;
        private readonly Dictionary<string, MacroSequence> sequences;
        private readonly Dictionary<string, MacroTrigger> triggers;
        private readonly Dictionary<string, object> globalVariables;
        private readonly Dictionary<string, Func<Dictionary<string, object>, object>> customFunctions;
        private readonly Queue<MacroSequence> executionQueue;
        private readonly CancellationTokenSource cancellationTokenSource;
        
        private readonly object executionLock = new object();
        private bool isSystemRunning = false;
        private Task executionTask;

        public event EventHandler<MacroExecutedEventArgs> MacroExecuted;
        public event EventHandler<MacroErrorEventArgs> MacroError;
        public event EventHandler<MacroTriggerEventArgs> MacroTriggered;

        public AdvancedMacroSystem(DS4Device device)
        {
            this.device = device ?? throw new ArgumentNullException(nameof(device));
            this.sequences = new Dictionary<string, MacroSequence>();
            this.triggers = new Dictionary<string, MacroTrigger>();
            this.globalVariables = new Dictionary<string, object>();
            this.customFunctions = new Dictionary<string, Func<Dictionary<string, object>, object>>();
            this.executionQueue = new Queue<MacroSequence>();
            this.cancellationTokenSource = new CancellationTokenSource();
            
            InitializeBuiltInFunctions();
            StartExecutionSystem();
        }

        /// <summary>
        /// Registers a macro sequence
        /// </summary>
        public void RegisterSequence(MacroSequence sequence)
        {
            sequences[sequence.Name] = sequence;
        }

        /// <summary>
        /// Registers a macro trigger
        /// </summary>
        public void RegisterTrigger(MacroTrigger trigger)
        {
            triggers[trigger.Name] = trigger;
        }

        /// <summary>
        /// Creates a simple key sequence macro
        /// </summary>
        public MacroSequence CreateKeySequence(string name, string[] keys, int[] delays = null)
        {
            var sequence = new MacroSequence { Name = name };
            
            for (int i = 0; i < keys.Length; i++)
            {
                // Key press
                sequence.Actions.Add(new MacroAction
                {
                    Type = MacroActionType.KeyPress,
                    Parameters = { { "Key", keys[i] } },
                    Description = $"Press {keys[i]}"
                });
                
                // Add delay if specified
                if (delays != null && i < delays.Length && delays[i] > 0)
                {
                    sequence.Actions.Add(new MacroAction
                    {
                        Type = MacroActionType.Delay,
                        Parameters = { { "Duration", delays[i] } },
                        Description = $"Wait {delays[i]}ms"
                    });
                }
                
                // Key release
                sequence.Actions.Add(new MacroAction
                {
                    Type = MacroActionType.KeyRelease,
                    Parameters = { { "Key", keys[i] } },
                    Description = $"Release {keys[i]}"
                });
            }
            
            RegisterSequence(sequence);
            return sequence;
        }

        /// <summary>
        /// Creates a conditional macro that executes based on game state
        /// </summary>
        public MacroSequence CreateConditionalMacro(string name, MacroCondition condition, 
            List<MacroAction> trueActions, List<MacroAction> falseActions = null)
        {
            var sequence = new MacroSequence 
            { 
                Name = name,
                Conditions = { condition }
            };
            
            // Add conditional action
            sequence.Actions.Add(new MacroAction
            {
                Type = MacroActionType.Condition,
                Parameters = 
                {
                    { "Condition", condition },
                    { "TrueActions", trueActions },
                    { "FalseActions", falseActions ?? new List<MacroAction>() }
                },
                Description = $"If {condition.Type} {condition.Operator} {condition.ExpectedValue}"
            });
            
            RegisterSequence(sequence);
            return sequence;
        }

        /// <summary>
        /// Creates a looping macro
        /// </summary>
        public MacroSequence CreateLoopingMacro(string name, List<MacroAction> actions, 
            int loopCount = -1, TimeSpan? loopDelay = null)
        {
            var sequence = new MacroSequence 
            { 
                Name = name,
                LoopCount = loopCount,
                LoopIndefinitely = loopCount == -1
            };
            
            sequence.Actions.Add(new MacroAction
            {
                Type = MacroActionType.Loop,
                Parameters = 
                {
                    { "Actions", actions },
                    { "Count", loopCount },
                    { "Delay", loopDelay ?? TimeSpan.FromMilliseconds(100) }
                },
                Description = $"Loop {(loopCount == -1 ? "indefinitely" : loopCount.ToString())} times"
            });
            
            RegisterSequence(sequence);
            return sequence;
        }

        /// <summary>
        /// Checks controller state and triggers appropriate macros
        /// </summary>
        public void ProcessControllerState(DS4State currentState, DS4State previousState)
        {
            foreach (var trigger in triggers.Values.Where(t => t.IsEnabled))
            {
                if (IsTriggerActivated(trigger, currentState, previousState))
                {
                    ExecuteSequence(trigger.AssociatedSequence);
                    trigger.LastTriggered = DateTime.UtcNow;
                    MacroTriggered?.Invoke(this, new MacroTriggerEventArgs(trigger));
                }
            }
        }

        /// <summary>
        /// Executes a macro sequence by name
        /// </summary>
        public async Task<bool> ExecuteSequence(string sequenceName)
        {
            if (!sequences.ContainsKey(sequenceName))
                return false;

            var sequence = sequences[sequenceName];
            
            // Check cooldown
            if (DateTime.UtcNow - sequence.LastExecuted < sequence.Cooldown)
                return false;
            
            // Check conditions
            if (!EvaluateConditions(sequence.Conditions))
                return false;

            lock (executionLock)
            {
                executionQueue.Enqueue(sequence);
            }
            
            return true;
        }

        /// <summary>
        /// Stops execution of a running macro
        /// </summary>
        public void StopSequence(string sequenceName)
        {
            if (sequences.ContainsKey(sequenceName))
            {
                sequences[sequenceName].IsRunning = false;
            }
        }

        /// <summary>
        /// Sets a global variable value
        /// </summary>
        public void SetVariable(string name, object value)
        {
            globalVariables[name] = value;
        }

        /// <summary>
        /// Gets a global variable value
        /// </summary>
        public T GetVariable<T>(string name, T defaultValue = default(T))
        {
            if (globalVariables.ContainsKey(name) && globalVariables[name] is T)
            {
                return (T)globalVariables[name];
            }
            return defaultValue;
        }

        /// <summary>
        /// Registers a custom function
        /// </summary>
        public void RegisterFunction(string name, Func<Dictionary<string, object>, object> function)
        {
            customFunctions[name] = function;
        }

        private void InitializeBuiltInFunctions()
        {
            // Battery level function
            RegisterFunction("getBatteryLevel", (args) => device.Battery);
            
            // Time-based functions
            RegisterFunction("getHour", (args) => DateTime.Now.Hour);
            RegisterFunction("getMinute", (args) => DateTime.Now.Minute);
            RegisterFunction("getDayOfWeek", (args) => DateTime.Now.DayOfWeek.ToString());
            
            // Controller state functions
            RegisterFunction("isButtonPressed", (args) =>
            {
                var buttonName = args.GetValueOrDefault("button", "").ToString();
                // Would need to implement button state checking
                return false;
            });
            
            // Math functions
            RegisterFunction("add", (args) => 
            {
                var a = Convert.ToDouble(args.GetValueOrDefault("a", 0));
                var b = Convert.ToDouble(args.GetValueOrDefault("b", 0));
                return a + b;
            });
            
            RegisterFunction("random", (args) =>
            {
                var min = Convert.ToInt32(args.GetValueOrDefault("min", 0));
                var max = Convert.ToInt32(args.GetValueOrDefault("max", 100));
                return new Random().Next(min, max);
            });
        }

        private void StartExecutionSystem()
        {
            isSystemRunning = true;
            executionTask = Task.Run(async () =>
            {
                while (isSystemRunning && !cancellationTokenSource.Token.IsCancellationRequested)
                {
                    MacroSequence sequenceToExecute = null;
                    
                    lock (executionLock)
                    {
                        if (executionQueue.Count > 0)
                        {
                            sequenceToExecute = executionQueue.Dequeue();
                        }
                    }
                    
                    if (sequenceToExecute != null)
                    {
                        await ExecuteSequenceInternal(sequenceToExecute);
                    }
                    
                    await Task.Delay(10, cancellationTokenSource.Token); // Small delay to prevent busy waiting
                }
            }, cancellationTokenSource.Token);
        }

        private async Task ExecuteSequenceInternal(MacroSequence sequence)
        {
            try
            {
                sequence.IsRunning = true;
                sequence.LastExecuted = DateTime.UtcNow;
                
                var loopCount = sequence.LoopIndefinitely ? int.MaxValue : sequence.LoopCount;
                
                for (int loop = 0; loop < loopCount && sequence.IsRunning; loop++)
                {
                    foreach (var action in sequence.Actions)
                    {
                        if (!sequence.IsRunning || !action.IsEnabled)
                            break;
                            
                        await ExecuteAction(action, sequence.Variables);
                    }
                    
                    if (sequence.LoopCount > 1 && loop < loopCount - 1)
                    {
                        await Task.Delay(100); // Small delay between loops
                    }
                }
                
                sequence.IsRunning = false;
                MacroExecuted?.Invoke(this, new MacroExecutedEventArgs(sequence, true));
            }
            catch (Exception ex)
            {
                sequence.IsRunning = false;
                MacroError?.Invoke(this, new MacroErrorEventArgs(sequence, ex));
            }
        }

        private async Task ExecuteAction(MacroAction action, Dictionary<string, object> variables)
        {
            switch (action.Type)
            {
                case MacroActionType.KeyPress:
                    var key = action.Parameters.GetValueOrDefault("Key", "").ToString();
                    // Implement key press logic
                    break;
                    
                case MacroActionType.KeyRelease:
                    var releaseKey = action.Parameters.GetValueOrDefault("Key", "").ToString();
                    // Implement key release logic
                    break;
                    
                case MacroActionType.Delay:
                    var duration = Convert.ToInt32(action.Parameters.GetValueOrDefault("Duration", 100));
                    await Task.Delay(duration);
                    break;
                    
                case MacroActionType.Condition:
                    var condition = (MacroCondition)action.Parameters.GetValueOrDefault("Condition");
                    var trueActions = (List<MacroAction>)action.Parameters.GetValueOrDefault("TrueActions");
                    var falseActions = (List<MacroAction>)action.Parameters.GetValueOrDefault("FalseActions");
                    
                    if (EvaluateCondition(condition))
                    {
                        foreach (var trueAction in trueActions)
                        {
                            await ExecuteAction(trueAction, variables);
                        }
                    }
                    else if (falseActions != null)
                    {
                        foreach (var falseAction in falseActions)
                        {
                            await ExecuteAction(falseAction, variables);
                        }
                    }
                    break;
                    
                case MacroActionType.Variable:
                    var varName = action.Parameters.GetValueOrDefault("Name", "").ToString();
                    var varValue = action.Parameters.GetValueOrDefault("Value");
                    variables[varName] = varValue;
                    break;
                    
                case MacroActionType.Function:
                    var funcName = action.Parameters.GetValueOrDefault("Function", "").ToString();
                    if (customFunctions.ContainsKey(funcName))
                    {
                        var funcArgs = (Dictionary<string, object>)action.Parameters.GetValueOrDefault("Arguments", 
                            new Dictionary<string, object>());
                        var result = customFunctions[funcName](funcArgs);
                        
                        var resultVar = action.Parameters.GetValueOrDefault("ResultVariable", "").ToString();
                        if (!string.IsNullOrEmpty(resultVar))
                        {
                            variables[resultVar] = result;
                        }
                    }
                    break;
            }
        }

        private bool IsTriggerActivated(MacroTrigger trigger, DS4State currentState, DS4State previousState)
        {
            // Simple implementation - would need to be expanded for full button combinations
            return false; // Placeholder
        }

        private bool EvaluateConditions(List<MacroCondition> conditions)
        {
            return conditions.All(EvaluateCondition);
        }

        private bool EvaluateCondition(MacroCondition condition)
        {
            if (condition == null) return true;
            
            object actualValue = GetConditionValue(condition);
            bool result = CompareValues(actualValue, condition.ExpectedValue, condition.Operator);
            
            return condition.IsInverted ? !result : result;
        }

        private object GetConditionValue(MacroCondition condition)
        {
            return condition.Type switch
            {
                MacroConditionType.BatteryLevel => device.Battery,
                MacroConditionType.TimeBased => DateTime.Now.Hour,
                MacroConditionType.VariableValue => globalVariables.GetValueOrDefault(condition.Parameter),
                _ => null
            };
        }

        private bool CompareValues(object actual, object expected, string operatorType)
        {
            if (actual == null || expected == null) return false;
            
            return operatorType.ToLower() switch
            {
                "equals" => actual.Equals(expected),
                "greater" => Convert.ToDouble(actual) > Convert.ToDouble(expected),
                "less" => Convert.ToDouble(actual) < Convert.ToDouble(expected),
                "contains" => actual.ToString().Contains(expected.ToString()),
                _ => false
            };
        }

        public void Dispose()
        {
            isSystemRunning = false;
            cancellationTokenSource.Cancel();
            executionTask?.Wait(TimeSpan.FromSeconds(5));
            cancellationTokenSource.Dispose();
        }
    }

    // Event argument classes
    public class MacroExecutedEventArgs : EventArgs
    {
        public MacroSequence Sequence { get; }
        public bool Success { get; }

        public MacroExecutedEventArgs(MacroSequence sequence, bool success)
        {
            Sequence = sequence;
            Success = success;
        }

        /// <summary>
        /// Executes a macro by name asynchronously
        /// </summary>
        public async Task ExecuteMacroAsync(string macroName)
        {
            if (string.IsNullOrEmpty(macroName)) return;

            var macro = registeredMacros.Values.FirstOrDefault(m => m.Name.Equals(macroName, StringComparison.OrdinalIgnoreCase));
            if (macro != null)
            {
                await ExecuteMacro(macro);
            }
        }

        /// <summary>
        /// Processes controller state changes for macro triggers
        /// </summary>
        public void ProcessControllerState(DS4State currentState, DS4State previousState)
        {
            if (currentState == null) return;

            // Check for macro triggers based on state changes
            foreach (var macro in registeredMacros.Values.Where(m => m.IsActive))
            {
                if (macro.Trigger != null && ShouldTriggerMacro(macro.Trigger, currentState, previousState))
                {
                    _ = Task.Run(() => ExecuteMacro(macro));
                }
            }
        }

        private bool ShouldTriggerMacro(MacroTrigger trigger, DS4State currentState, DS4State previousState)
        {
            // Simple implementation - can be expanded
            return trigger.TriggerType == MacroTriggerType.ButtonPress;
        }
    }

    public class MacroErrorEventArgs : EventArgs
    {
        public MacroSequence Sequence { get; }
        public Exception Error { get; }

        public MacroErrorEventArgs(MacroSequence sequence, Exception error)
        {
            Sequence = sequence;
            Error = error;
        }
    }

    public class MacroTriggerEventArgs : EventArgs
    {
        public MacroTrigger Trigger { get; }

        public MacroTriggerEventArgs(MacroTrigger trigger)
        {
            Trigger = trigger;
        }
    }
}
