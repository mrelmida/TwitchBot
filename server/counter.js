let ws_counter; // WebSocket connection
let conn_err_counter;
let content_counter;
let static_url_counter = 'http://127.0.0.1:40000/'; // Static default address
let static_ws_counter = 'ws://127.0.0.1:40000/counter';

function loaded() {
  conn_err_counter = document.getElementById('conn_err');
  content_counter = document.getElementById('content');

  connect();

  setInterval(function () {
    // try to reconnect every 5 sec
    if (ws_counter.readyState != 1) {
      if (ws_counter != null && ws_counter.readyState == 0) {
        ws_counter.close();
      }
      connect();
    }
  }, 5000);
}

// Check if the html page was loaded from the server or static file
if (window.location.protocol != "file:") {
  static_url_counter = ''; // For html received from the server reset static url and ws
  static_ws_counter = '';
}
window.addEventListener('load', loaded);

function connect() {
  if (static_ws_counter.length > 0) {
    ws_counter = new WebSocket(static_ws_counter);
  } else {
    ws_counter = new WebSocket('ws://' + window.location.hostname + ':' + window.location.port + '/counter');
  }

  ws_counter.addEventListener('open', () => {
    console.log('Counter WebSocket connection established!');
    conn_err_counter.hidden = true;
    content_counter.hidden = false;
  });

  ws_counter.addEventListener('close', () => {
    console.log('Counter WebSocket connection closed!');
    conn_err_counter.hidden = false;
    content_counter.hidden = true;
    clear_content();
  });

  ws_counter.addEventListener('message', (e) => {
    parse_message(e.data);
  });

  ws_counter.addEventListener('error', (err) => {
    console.error('Counter WebSocket encountered error: ', err.message);
    ws_counter.close();
  });
}

function parse_message(d) {
  let data = JSON.parse(d);
  // console.log(data);

  clear_content();

  for (var i = 0; i < data.length; i++) {
    var d = data[i];
    for (var j = 0; j < 3; j++) {
      var div = document.createElement('div');
      if (j == 2) {
        var img = document.createElement('img');
        img.src = static_url_counter + d[j];
        div.appendChild(img);
      } else {
        var text = document.createElement('p');
        text.textContent = d[j];
        div.appendChild(text);
      }
      content_counter.appendChild(div);
    }
  }
}

function clear_content() {
  content_counter.innerHTML = '';
}