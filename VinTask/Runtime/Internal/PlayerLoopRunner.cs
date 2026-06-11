using System;

namespace Cysharp.Threading.Tasks.Internal
{
    internal sealed class PlayerLoopRunner
    {
        const int INITIAL_SIZE = 16;

        readonly object _isRunningAndQueueLock = new object();
        readonly object _arrayLock = new object();

        int _tail = 0;
        bool _isRunning = false;
        IPlayerLoopItem?[] _loopItems = new IPlayerLoopItem?[INITIAL_SIZE];
        MinimumQueue<IPlayerLoopItem?> _waitQueue = new MinimumQueue<IPlayerLoopItem?>(INITIAL_SIZE);

        public PlayerLoopRunner()
        {
        }

        public void AddAction(IPlayerLoopItem? item)
        {
            lock (_isRunningAndQueueLock)
            {
                if (_isRunning)
                {
                    _waitQueue.Enqueue(item);
                    return;
                }
            }

            lock (_arrayLock)
            {
                if (_loopItems.Length == _tail)
                {
                    Array.Resize(ref _loopItems, checked(_tail * 2));
                }
                _loopItems[_tail++] = item;
            }
        }

        public int Clear()
        {
            lock (_arrayLock)
            {
                int rest = 0;

                for (int index = 0; index < _loopItems.Length; index++)
                {
                    if (_loopItems[index] != null)
                    {
                        rest++;
                    }

                    _loopItems[index] = null;
                }

                _tail = 0;
                return rest;
            }
        }

        public void Run()
        {
            lock (_isRunningAndQueueLock)
            {
                _isRunning = true;
            }

            lock (_arrayLock)
            {
                int j = _tail - 1;

                for (int i = 0; i < _loopItems.Length; i++)
                {
                    IPlayerLoopItem? action = _loopItems[i];
                    if (action != null)
                    {
                        try
                        {
                            if (!action.MoveNext())
                            {
                                _loopItems[i] = null;
                            }
                            else
                            {
                                continue;
                            }
                        }
                        catch (Exception ex)
                        {
                            _loopItems[i] = null;
                            try
                            {
                                UniTaskScheduler.PublishUnobservedTaskException(ex);
                            }
                            catch { }
                        }
                    }

                    while (i < j)
                    {
                        IPlayerLoopItem? fromTail = _loopItems[j];
                        if (fromTail != null)
                        {
                            try
                            {
                                if (!fromTail.MoveNext())
                                {
                                    _loopItems[j] = null;
                                    j--;
                                    continue;
                                }
                                else
                                {
                                    _loopItems[i] = fromTail;
                                    _loopItems[j] = null;
                                    j--;
                                    goto NEXT_LOOP;
                                }
                            }
                            catch (Exception ex)
                            {
                                _loopItems[j] = null;
                                j--;
                                try
                                {
                                    UniTaskScheduler.PublishUnobservedTaskException(ex);
                                }
                                catch { }
                                continue;
                            }
                        }
                        else
                        {
                            j--;
                        }
                    }

                    _tail = i;
                    break;

                    NEXT_LOOP:
                    continue;
                }

                lock (_isRunningAndQueueLock)
                {
                    _isRunning = false;
                    while (_waitQueue.Count != 0)
                    {
                        if (_loopItems.Length == _tail)
                        {
                            Array.Resize(ref _loopItems, checked(_tail * 2));
                        }
                        _loopItems[_tail++] = _waitQueue.Dequeue();
                    }
                }
            }
        }
    }
}
