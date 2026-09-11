# Third-party notices

## English frequency lexicon

SmartInput includes `en_50k.txt` from the [FrequencyWords](https://github.com/hermitdave/FrequencyWords)
project, Copyright (c) 2016 Hermit Dave, under the MIT License:

```text
MIT License

Copyright (c) 2016 Hermit Dave

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Russian frequency lexicon

SmartInput includes `ru_50k.csv` from
[Russian Word Frequency Lists for Children](https://github.com/Digital-Pushkin-Lab/Russian-Word-Frequency-Lists-for-Children),
published under CC0-1.0.

## Russian word-form index

The conservative Russian word-form guard is generated from the `ru_RU.txt.gz`
CSpell list published by [Goudron/ru-spelling-dictionary](https://github.com/Goudron/ru-spelling-dictionary).
The upstream source is available under MPL-2.0 and includes the preserved
Lebedev-family BSD-style notice. SmartInput uses the generated index only to
avoid automatic text changes for recognised Russian forms; it does not send
words to a network service or expose the source list in diagnostics.
