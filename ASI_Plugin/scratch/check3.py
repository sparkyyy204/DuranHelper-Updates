import sys
text = open('c:/Users/vmaki/Desktop/test/main.cpp', 'r', encoding='utf-8').read()
in_string = False
in_char = False
for i, c in enumerate(text):
    if c == '\\': continue # very naive skip
    if c == '"' and not in_char:
        in_string = not in_string
    elif c == "'" and not in_string:
        in_char = not in_char

print('in_string:', in_string, 'in_char:', in_char)
