import sys
with open('c:/Users/vmaki/Desktop/test/gui.cpp', 'r', encoding='utf-8') as f:
    text = f.read()

stack = []
for i, line in enumerate(text.split('\n')):
    line = line.split('//')[0]
    for c in line:
        if c == '{': stack.append(i+1)
        elif c == '}': 
            if stack: stack.pop()
            else: print('Unmatched } at line', i+1)

if stack:
    print('Unmatched { at lines:', stack)
else:
    print('gui.cpp: All braces matched!')
