﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class LinuxAppServiceFileLogger
    {
        private readonly string _logFileName;
        private readonly string _logFileDirectory;
        private readonly string _logFilePath;
        private readonly BlockingCollection<string> _buffer;
        private readonly List<string> _currentBatch;
        private readonly ILinuxAppServiceFileSystem _fileSystem;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private Task _outputTask;

        public LinuxAppServiceFileLogger(string logFileName, string logFileDirectory, ILinuxAppServiceFileSystem fileSystem, bool startOnCreate = true)
        {
            _logFileName = logFileName;
            _logFileDirectory = logFileDirectory;
            _logFilePath = Path.Combine(_logFileDirectory, _logFileName + ".log");
            _buffer = new BlockingCollection<string>(new ConcurrentQueue<string>());
            _currentBatch = new List<string>();
            _fileSystem = fileSystem;
            _cancellationTokenSource = new CancellationTokenSource();

            if (startOnCreate)
            {
                Start();
            }
        }

        // Maximum number of files
        public int MaxFileCount { get; set; } = 3;

        // Maximum size of individual log file in MB
        public int MaxFileSizeMb { get; set; } = 10;

        // Maximum time between successive flushes (seconds)
        public int FlushFrequencySeconds { get; set; } = 30;

        public virtual void Log(string message)
        {
            try
            {
                _buffer.Add(message);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        public void Start()
        {
            if (_outputTask == null)
            {
                _outputTask = Task.Factory.StartNew(ProcessLogQueue, null, TaskCreationOptions.LongRunning);
            }
        }

        public void Stop(TimeSpan timeSpan)
        {
            _cancellationTokenSource.Cancel();

            try
            {
                _outputTask?.Wait(timeSpan);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        public virtual async Task ProcessLogQueue(object state)
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                await InternalProcessLogQueue();
                await Task.Delay(TimeSpan.FromSeconds(FlushFrequencySeconds));
            }
            // ReSharper disable once FunctionNeverReturns
        }

        // internal for unittests
        internal async Task InternalProcessLogQueue()
        {
            string currentMessage;
            while (_buffer.TryTake(out currentMessage))
            {
                _currentBatch.Add(currentMessage);
            }

            if (_currentBatch.Any())
            {
                try
                {
                    await WriteLogs(_currentBatch);
                }
                catch (Exception)
                {
                    // Ignored
                }

                _currentBatch.Clear();
            }
        }

        private async Task WriteLogs(IEnumerable<string> currentBatch)
        {
            _fileSystem.CreateDirectory(_logFileDirectory);

            if (_fileSystem.FileExists(_logFilePath))
            {
                if (_fileSystem.GetFileSizeBytes(_logFilePath) / (1024 * 1024) >= MaxFileSizeMb)
                {
                    RollFiles();
                }
            }

            await _fileSystem.AppendLogs(_logFilePath, currentBatch);
        }

        private void RollFiles()
        {
            // Rename current file to older file.
            // Empty current file.
            // Delete oldest file if exceeded configured max no. of files.

            _fileSystem.MoveFile(_logFilePath, GetCurrentFileName(DateTime.UtcNow));

            var fileInfos = _fileSystem.ListFiles(_logFileDirectory, _logFileName + "*", SearchOption.TopDirectoryOnly);

            if (fileInfos.Length >= MaxFileCount)
            {
                var oldestFile = fileInfos.OrderByDescending(f => f.Name).Last();
                _fileSystem.DeleteFile(oldestFile);
            }
        }

        public string GetCurrentFileName(DateTime dateTime)
        {
            return Path.Combine(_logFileDirectory, $"{_logFileName}{dateTime:yyyyMMddHHmmss}.log");
        }
    }
}
