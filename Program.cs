// This example is based on the sample code in "How to recognize speech" in
//     https://learn.microsoft.com/en-us/azure/cognitive-services/speech-service/how-to-recognize-speech?pivots=programming-language-csharp

using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System.Text;

class Program
{
    static int Main(string[] args)
    {
        // Parsing the command-line options.
        if (args.Length < 2)
        {
            Console.WriteLine("======================================================================================");
            Console.WriteLine("Please provide the *.wav filename and the language/locality for the expected language,");
            Console.WriteLine("    For instance, CsharpAzureSpeechCaption.exe sample.wav en-US");
            return 1;
        }
        var speechSourceFile = args[0];
        var targetLanguage = args[1];

        // Parsing the Azure credentials via Windows environmental variables.
        var speechKey = Environment.GetEnvironmentVariable("SPEECH_KEY");
        var speechRegion = Environment.GetEnvironmentVariable("SPEECH_REGION");
        if (speechKey is null || speechRegion is null)
        {
            Console.WriteLine("=======================================================================================");
            Console.WriteLine("Please set the environmental variables SPEECH_KEY and SPEECH_REGION for proper billing.");
            return 2;
        }
        // TODO: We have all the input parameters.  Verify them before use.

        var speechConfig = SpeechConfig.FromSubscription(speechKey, speechRegion);
        speechConfig.SpeechRecognitionLanguage = targetLanguage;
        Console.OutputEncoding = Encoding.Unicode; // For UTF-16; this is for displaying some fonts like Japanese.

        RecognitionFromWav(speechConfig, speechSourceFile).GetAwaiter().GetResult();

        return 0;
    }

    async static Task RecognitionFromWav(SpeechConfig speechConfig, string speechSourceFile)
    {
        var stopRecognition = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (var audioInput = AudioConfig.FromWavFileInput(speechSourceFile))
        {
            using (var speechRecognizer = new SpeechRecognizer(speechConfig, audioInput))
            {
                // Subscribing to the miscellaneous events.
                speechRecognizer.Recognizing += (s, e) =>
                {
                    // Recognizing events give a partial result (as compared to Recognized events)
                    Console.WriteLine($"      RECOGNIZING: Text= {e.Result.Text}");
                };

                speechRecognizer.Recognized += (s, e) =>
                {
                    if (e.Result.Reason == ResultReason.RecognizedSpeech)
                    {
                        Console.WriteLine($"RECOGNIZED: Text= {e.Result.Text}");
                    }
                    else
                    {
                        Console.WriteLine($"RECOGNIZED UNLISTED REASON: Text={e.Result.Reason}.");
                    }
                    Console.WriteLine("");
                };

                speechRecognizer.Canceled += (s, e) =>
                {
                    Console.WriteLine($"CANCELED: Reason={e.Reason}");

                    // * If the specified region is invalid, we have
                    //       ErrorCode = ConnectionFailure
                    //       ErrorDetails = Connection failed (no connection to the remote host). Internal error: 1.
                    //                      Error details: Failed with error: WS_OPEN_ERROR_UNDERLYING_IO_OPEN_FAILED
                    // * If the specified speech key is invalid, we have
                    //       ErrorCode = AuthenticationFailure
                    //       ErrorDetails = WebSocket upgrade failed: Authentication error (401). Please check subscription information and region name.
                    // * If the specified language is invalid (say, "en-JP"), we have
                    //       ErrorCode = BadRequest
                    //       ErrorDetails = WebSocket upgrade failed: Bad request (400). Error details: Failed with HTTP 400 Bad Request ......
                    // The following errors are observed though we do not yet figure out the reasons.
                    // * ErrorCode = BadRequest
                    //   ErrorDetails = ErrorDetails=Connection was closed by the remote host. Error code: 1007. Error details: Quota exceeded.
                    if (e.Reason == CancellationReason.Error)
                    {
                        Console.WriteLine($"CANCELED: ErrorCode={e.ErrorCode}");
                        Console.WriteLine($"CANCELED: ErrorDetails={e.ErrorDetails}");
                    }

                    stopRecognition.TrySetResult(0);
                };

                speechRecognizer.SessionStarted += (s, e) =>
                {
                    Console.WriteLine($"==========================");
                    Console.WriteLine($"    Session started event.");
                    Console.WriteLine($"==========================");
                    Console.WriteLine("\n\n");
                };

                speechRecognizer.SessionStopped += (s, e) =>
                {
                    Console.WriteLine("\n\n");
                    Console.WriteLine($"==========================");
                    Console.WriteLine($"    Session stopped event.");
                    Console.WriteLine($"Stop recognition.");
                    stopRecognition.TrySetResult(0);
                };

                // Starts continuous recognition.
                await speechRecognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);

                // Waits for completion.
                // Use Task.WaitAny to keep the task rooted.
                Task.WaitAny(new[] { stopRecognition.Task });

                // Stops recognition.
                await speechRecognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
            }
        }
    }
}